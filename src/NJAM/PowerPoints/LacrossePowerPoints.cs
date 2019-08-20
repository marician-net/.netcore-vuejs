﻿using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using VSAND.Common;
using VSAND.Data.Entities;
using VSAND.Data.Repositories;
using VSAND.Interfaces;

namespace NJAM.PowerPoints
{
    public class LacrossePowerPoints : IType, IPowerPoints, IConvertible
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public int TeamId { get; set; } = 0;

        public List<VsandGameReport> GameReports { get; set; }

        private DateTime _startDate = DateHelp.SqlMinDate;
        public DateTime StartDate
        {
            get {
                return new DateTime(_startDate.Year, _startDate.Month, _startDate.Day, 0, 0, 0);
            }
            set {
                _startDate = value;
            }
        }

        private DateTime _endDate = DateHelp.SqlMaxDate;
        public DateTime EndDate
        {
            get {
                return new DateTime(_endDate.Year, _endDate.Month, _endDate.Day, 23, 59, 59);
            }
            set {
                _endDate = value;
            }
        }

        public int IncludeGames { get; set; } = 13;
        public int EligibleGamesCount { get; set; } = 0;
        public double PowerPoints { get; set; } = 0;

        private readonly IUnitOfWork _uow;

        public LacrossePowerPoints(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public LacrossePowerPoints(IUnitOfWork uow, int teamId, List<VsandGameReport> oGameReports, DateTime dStart, DateTime dEnd)
        {
            _uow = uow;

            this.TeamId = teamId;
            this.GameReports = oGameReports;
            this.StartDate = dStart;
            this.EndDate = dEnd;
        }

        public LacrossePowerPoints(IUnitOfWork uow, int teamId, List<VsandGameReport> oGameReports, DateTime dStart, DateTime dEnd, int maxGames)
        {
            _uow = uow;

            this.TeamId = teamId;
            this.GameReports = oGameReports;
            this.StartDate = dStart;
            this.EndDate = dEnd;
            this.IncludeGames = maxGames;
        }

        public string InputMask()
        {
            return "";
        }

        public decimal ToDecimal()
        {
            double dPowerPoints = 0;
            int iIncludeGames = this.IncludeGames;

            // C. Power Point Calculation Procedure: 
            // 1. Quality Points - Each school will receive the following "Quality Points" for a win or tie from the first eight games: 
            // Win = 6 points; Tie = 3 points; Lose = 0 points 

            // 2. Group Points - Each school will receive "Group Points" from a team they defeated or tied from the first eight games: 
            // Group IV = 4 points 
            // Group III = 3 points 
            // Group II = 2 points 
            // Group I = 1 points 
            // Note: Once the "Football Group Classification" is determined, 
            // there will be no adjustments to this classification when playing teams 
            // (public or non-public) from other sections. When playing teams from out 
            // of state, the group size will be determined based on the enrollment range 
            // of the winning teams section. 

            // 3. Residual Points - Each team will receive residual points from one of the categories below based on the result of the game. 
            // A. Each school will receive "Residual Points" from a team they defeated. For each win or tie your opponent has from the first eight games, you will receive the following (see examples 1 && 2): 
            // Win = 3 points; Tie = 1.5 points; Lose = 0 points 
            // B. Each school will receive "Residual Points" from a team they tied. For each win or tie your opponent has from the first eight games, you will receive the following (see examples 3 && 4): 
            // Win = 1.5 points; Tie = .75 points (See note below); Lose =0 points 
            // Note: No points will be rewarded when calculating "Residual Points" when your game is the opponent's only tie, since you already have been awarded points for that tie. 
            // C. Each school will receive "Residual Points" from a team they lost to. For each win your opponent has that defeated you (not including your game) from the first eight games, you will receive the following (see example 5): 
            // Win = 1 point; Tie = 0 points; Lose = 0 points 

            // Example(1(win))
            // Team A defeated Team B (Group IV) 
            // Team A record 6-2 
            // Team B record 7-1 5 
            // Team A would earn a total of 31 points for the Team B win. (6 points for the win, 4 group points, 21 residual points) 

            // Example(2(win))
            // Team A defeated Team B (Group IV) 
            // Team A record 6-2 
            // Team B record 6-1-1 
            // Team A would earn a total of 29.5 points for the Team B win. (6 points for the win, 4 group points, 19.5 residual points) 

            // Example(3(tie))
            // Team A ties Team B (Group IV) 
            // Team A record 6-1-1 
            // Team B record 5-2-1 
            // Team A would earn a total of 14.5 points for the Team B tie. (3 points for the tie, 4 group points, 7.5 residual points) 

            // Example(4(tie))
            // Team A ties Team B (Group IV) 
            // Team A record 6-1-1 
            // Team B record 5-1-2 
            // Team A would earn a total of 15.25 points for the Team B tie. (3 points for the tie, 4 group points, 8.25 residual points) 

            // Example(5(loss))
            // Non-Public (III) Team A defeated Public Team B 
            // Team B record 6-2 
            // Team A record 7-1 
            // Team B would earn a total of 6 points from the Team A defeat. (6 residual points)


            List<VsandGameReport> oEligibleGames = this.GameReports.Where(gr => gr.Deleted == false && gr.Final == true &&  gr.PPEligible == true).OrderBy(gt => gt.GameDate).Take(iIncludeGames).ToList();

            StringBuilder oCalcSb = new StringBuilder();
            oCalcSb.AppendLine("<table width=\"100%\" border=\"0\" cellpadding=\"5\">");
            oCalcSb.AppendLine("<caption>Limit first " + this.IncludeGames.ToString() + " games" + (this.StartDate > DateHelp.SqlMinDate ? " between " + this.StartDate.ToString("%M/%d/yyyy") + " and " + this.EndDate.ToString("%M/%d/yyyy") : "") + "</caption>");
            oCalcSb.AppendLine("<thead><tr><th>Date</th><th>Opp</th><th>Result</th><th>Quality</th><th>Group</th><th>Residual</th><th>PP</th></tr></thead>");
            oCalcSb.AppendLine("<tbody>");
            double dLowPpVal = 999;
            int iRunQuality = 0;
            int iRunGroup = 0;
            double dRunResidual = 0;

            foreach (VsandGameReport oGr in oEligibleGames)
            {
                var oGameTeams = oGr.Teams;
                if (oGameTeams.Count == 2 && oGr.GameDate >= StartDate && oGr.GameDate <= EndDate)
                {
                    int iQualityPoints = 0;
                    int iGroupPoints = 0;
                    double dResidualPoints = 0;

                    int iGameReportId = oGr.GameReportId;
                    VsandGameReportTeam oTeam = oGameTeams.FirstOrDefault(gt => gt.TeamId == TeamId);
                    double dTeamScore = oTeam.FinalScore;

                    VsandGameReportTeam oOpp = oGameTeams.FirstOrDefault(gt => gt.TeamId != TeamId);
                    double dOppScore = oOpp.FinalScore;

                    int iOppTeamId = oOpp.TeamId;
                    string sOppGroup = "";
                    int iGroupVal = 0;
                    int iOppWins = 0;
                    int iOppLosses = 0;
                    int iOppTies = 0;
                    bool bOutOfState = false;
                    string sOppName = "";
                    bool bPrivate = false;
                    bool bOppPrivate = false;

                    if (oTeam.Team.School.PrivateSchool.HasValue && oTeam.Team.School.PrivateSchool.Value)
                    {
                        bPrivate = true;
                    }

                    int iOppRecordWins = 0;
                    int iOppRecordLosses = 0;
                    int iOppRecordTies = 0;

                    Regex oNumRe = new Regex("[0-9]");

                    if (oOpp.Team != null)
                    {
                        sOppName = oOpp.Team.Name;
                        if (oOpp.Team.School.State != "NJ")
                        {
                            bOutOfState = true;
                        }
                        else if (oOpp.Team.School.PrivateSchool.HasValue && oOpp.Team.School.PrivateSchool.Value)
                        {
                            bOppPrivate = true;
                        }

                        VsandTeamCustomCode oOppGroup = null/* TODO Change to default(_) if this is not a reference type */;
                        if (!bOutOfState)
                        {
                            if (bPrivate && bOppPrivate)
                            {
                                oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName.Equals("Group", System.StringComparison.OrdinalIgnoreCase));
                                if (oOppGroup != null)
                                {
                                    sOppGroup = oOppGroup.CodeValue.ToLower().Replace("group", "").Trim();
                                    if (string.IsNullOrEmpty(sOppGroup) || !oNumRe.IsMatch(sOppGroup))
                                    {
                                        oOppGroup = null;
                                    }
                                }
                                if (oOppGroup == null)
                                {
                                    oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(cc => cc.CodeName.Equals("GroupExchange", System.StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            else
                            {
                                oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(cc => cc.CodeName.Equals("GroupExchange", System.StringComparison.OrdinalIgnoreCase));
                                if (oOppGroup != null)
                                {
                                    sOppGroup = oOppGroup.CodeValue.ToLower().Replace("group", "").Trim();
                                    if (string.IsNullOrEmpty(sOppGroup) || !oNumRe.IsMatch(sOppGroup))
                                    {
                                        oOppGroup = null;
                                    }
                                }
                                if (oOppGroup == null)
                                {
                                    oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName.Equals("Group", System.StringComparison.OrdinalIgnoreCase));
                                }
                            }

                            if (oOppGroup != null)
                            {
                                sOppGroup = oOppGroup.CodeValue.ToLower();
                            }
                        }
                        else
                        {
                            oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(cc => cc.CodeName.Equals("GroupExchange", System.StringComparison.OrdinalIgnoreCase));
                            if (oOppGroup != null)
                            {
                                sOppGroup = oOppGroup.CodeValue;
                            }
                        }

                        // -- Get their group value
                        switch (sOppGroup.ToLower())
                        {
                            case "group 4":
                                {
                                    iGroupVal = 4;
                                    break;
                                }

                            case "group 3":
                                {
                                    iGroupVal = 3;
                                    break;
                                }

                            case "group 2":
                                {
                                    iGroupVal = 2;
                                    break;
                                }

                            case "group 1":
                                {
                                    iGroupVal = 1;
                                    break;
                                }

                            default:
                                {
                                    sOppGroup = sOppGroup.ToLower().Replace("group", "").Trim();
                                    int.TryParse(sOppGroup, out iGroupVal);
                                    break;
                                }
                        }

                        // -- Get any write-in record that exists (regardless of in-state/out-of-state)
                        VsandTeamCustomCode oOppWinEx = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName == "OOSFinalWins");
                        if (oOppWinEx != null)
                        {
                            string sOppWins = oOppWinEx.CodeValue;
                            int.TryParse(sOppWins, out iOppWins);
                        }
                        VsandTeamCustomCode oOppLoseEx = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName == "OOSFinalLosses");
                        if (oOppLoseEx != null)
                        {
                            string sOppLosses = oOppLoseEx.CodeValue;
                            int.TryParse(sOppLosses, out iOppLosses);
                        }
                        VsandTeamCustomCode oOppTieEx = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName == "OOSFinalTies");
                        if (oOppTieEx != null)
                        {
                            string sOppTies = oOppTieEx.CodeValue;
                            int.TryParse(sOppTies, out iOppTies);
                        }

                        iOppRecordWins = iOppWins;
                        iOppRecordLosses = iOppLosses;
                        iOppRecordTies = iOppTies;

                        int iRecTotal = iOppWins + iOppTies + iOppLosses;
                        if (iRecTotal <= iIncludeGames)
                        {
                            // -- Don't need the loop anymore, we should only be using a deduction for THIS game
                            // Dim iOppRecordWinOffset As Integer = 0
                            // Dim iOppRecordLossOffset As Integer = 0
                            // Dim iOppRecordTieOffset As Integer = 0
                            // Dim oVsGames As List(Of VsandGameReport) = oEligibleGames.Where(Function(gr) gr.Teams.Any((Function(grt) grt.TeamId = iOppTeamId))).ToList()
                            // For Each oVsGame As VsandGameReport In oVsGames
                            // Dim oVSTeam As VsandGameReportTeam = oVsGame.Teams.FirstOrDefault(Function(grt) grt.TeamId = Me.TeamId)
                            // Dim oVSOpp As VsandGameReportTeam = oVsGame.Teams.FirstOrDefault(Function(grt) grt.TeamId = iOppTeamId)

                            // If oVSOpp.FinalScore > oVSTeam.FinalScore Then
                            // iOppRecordWinOffset = iOppRecordWinOffset - 1
                            // ElseIf oVSOpp.FinalScore = oVSTeam.FinalScore Then
                            // iOppRecordTieOffset = iOppRecordTieOffset - 1
                            // Else
                            // iOppRecordLossOffset = iOppRecordLossOffset - 1
                            // End If
                            // Next

                            if (dTeamScore.Equals(dOppScore))
                            {
                                iOppTies = iOppTies - 1;
                            }
                            else if (dTeamScore < dOppScore)
                            {
                                iOppWins = iOppWins - 1;
                            }

                            if (iOppWins < 0)
                            {
                                iOppWins = 0;
                            }

                            if (iOppTies < 0)
                            {
                                iOppTies = 0;
                            }
                        }
                        else
                        {
                            iOppWins = 0;
                            iOppTies = 0;
                            iOppLosses = 0;
                        }

                        if (!bOutOfState && (iOppWins + iOppLosses + iOppLosses) == 0)
                        {
                            List<VsandGameReport> oOppGameReports = null;

                            var oData = _uow.GameReports.List(
                                                            g => g.Teams.Any(gt => gt.Team.TeamId == iOppTeamId) && g.Deleted == false && g.Final == true && g.PPEligible == true && g.GameDate >= this.StartDate && g.GameDate <= this.EndDate,
                                                            x => x.OrderBy(g => g.GameDate),
                                                            new List<string> { "Teams" }
                                                        ).Result.Take(this.IncludeGames);

                            if (oData != null)
                            {
                                oOppGameReports = oData.ToList();
                            }


                            foreach (VsandGameReport oOppGr in oOppGameReports)
                            {
                                var oOppGameTeams = oOppGr.Teams;
                                VsandGameReportTeam oOppTeam = oOppGameTeams.FirstOrDefault(gt => gt.TeamId == iOppTeamId);
                                double dOppTeamScore = oOppTeam.FinalScore;

                                VsandGameReportTeam oOppOpp = oOppGameTeams.FirstOrDefault(gt => gt.TeamId != iOppTeamId);
                                double dOppOppScore = oOppOpp.FinalScore;

                                if (dOppTeamScore > dOppOppScore)
                                {
                                    iOppRecordWins = iOppRecordWins + 1;
                                    if (oOppGr.GameReportId != iGameReportId)
                                    {
                                        iOppWins = iOppWins + 1;
                                    }
                                }
                                else if (dOppOppScore > dOppTeamScore)
                                {
                                    iOppRecordLosses = iOppRecordLosses + 1;
                                    if (oOppGr.GameReportId != iGameReportId)
                                    {
                                        iOppLosses = iOppLosses + 1;
                                    }
                                }
                                else
                                {
                                    iOppRecordTies = iOppRecordTies + 1;
                                    if (oOppGr.GameReportId != iGameReportId)
                                    {
                                        iOppTies = iOppTies + 1;
                                    }
                                }
                            }
                        }
                    }

                    string sResult = "";
                    if (dTeamScore > dOppScore)
                    {
                        iQualityPoints = 6;
                        iGroupPoints = iGroupVal * 0;
                        dResidualPoints = (iOppWins * 3) + (iOppTies * 1.5);
                        sResult = "W " + dTeamScore + "-" + dOppScore;
                    }
                    else if (dTeamScore < dOppScore)
                    {
                        iQualityPoints = 0;
                        iGroupPoints = 0;
                        dResidualPoints = (iOppWins * 1);
                        sResult = "L " + dOppScore + "-" + dTeamScore;
                    }
                    else
                    {
                        iQualityPoints = 3;
                        iGroupPoints = iGroupVal * 0;
                        dResidualPoints = (iOppWins * 1.5) + (iOppTies * 0.75);
                        sResult = "T " + dTeamScore + "-" + dOppScore;
                    }

                    double dPPVal = iQualityPoints + iGroupPoints + dResidualPoints;
                    if (dPPVal < dLowPpVal)
                    {
                        dLowPpVal = dPPVal;
                    }

                    iRunQuality = iRunQuality + iQualityPoints;
                    iRunGroup = iRunGroup + iGroupPoints;
                    dRunResidual = dRunResidual + dResidualPoints;

                    oCalcSb.AppendLine("<tr><td class=\"center\">" + oGr.GameDate.ToString("%M/%d/yy") + "</td><td>" + oOpp.TeamName + " (" + iOppRecordWins + "-" + iOppRecordLosses + "-" + iOppRecordTies + ")</td><td>" + sResult + "</td><td class=\"center\">" + iQualityPoints + "</td><td class=\"center\">" + iGroupPoints + "</td><td class=\"center\">" + dResidualPoints + "</td><td class=\"center\">" + dPPVal + "</td></tr>");

                    dPowerPoints = dPowerPoints + iQualityPoints + iGroupPoints + dResidualPoints;
                }
            }

            oCalcSb.AppendLine("<tr><td colspan=\"3\" style=\"text-align:right;font-weight:bold;\">Totals:</td><td class=\"center\">" + iRunQuality + "</td><td class=\"center\">" + iRunGroup + "</td><td class=\"center\">" + dRunResidual + "</td><td class=\"center\">" + dPowerPoints + "</td></tr>");
            oCalcSb.AppendLine("</tbody></table>");
            try
            {
                // -- save the calculation for reference
                VsandGameReport oRefGame = oEligibleGames.FirstOrDefault();
                if (oRefGame != null)
                {
                    int iSchedYear = oRefGame.ScheduleYearId;
                    int iSport = oRefGame.SportId;
                    string sDir = "appdata/PPCalc/" + iSchedYear + "/" + iSport;
                    Directory.CreateDirectory(sDir);
                    File.WriteAllText(Path.Combine(sDir, TeamId + ".htm"), oCalcSb.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }

            PowerPoints = dPowerPoints;

            return (decimal)dPowerPoints;
        }

        public double TieBreak()
        {
            double dPPTB = 0;
            int iIncludeGames = this.IncludeGames;

            IEnumerable<VsandGameReport> oEligibleGames = this.GameReports.Where(gr => gr.Deleted == false && gr.Final == true && gr.PPEligible == true && gr.Teams.Count == 2).OrderBy(gt => gt.GameDate).Take(iIncludeGames);

            foreach (VsandGameReport oGR in oEligibleGames)
            {
                var oGameTeams = oGR.Teams;
                if (oGR.GameDate >= StartDate && oGR.GameDate <= EndDate)
                {
                    VsandGameReportTeam oOpp = oGameTeams.FirstOrDefault(gt => gt.TeamId != TeamId);
                    int iOppTeamId = oOpp.TeamId;
                    string sOppGroup = "";
                    int iGroupVal = 0;
                    int iOppWins = 0;
                    bool bOutOfState = false;
                    bool bPrivate = false;
                    bool bOppPrivate = false;

                    VsandGameReportTeam oTeam = oGameTeams.FirstOrDefault(gt => gt.TeamId == TeamId);

                    if (oTeam.Team.School.PrivateSchool.HasValue && oTeam.Team.School.PrivateSchool.Value)
                    {
                        bPrivate = true;
                    }

                    if (oOpp.Team != null)
                    {
                        if (oOpp.Team.School.State != "NJ")
                        {
                            bOutOfState = true;
                        }
                        else if (oOpp.Team.School.PrivateSchool.HasValue && oOpp.Team.School.PrivateSchool.Value)
                        {
                            bOppPrivate = true;
                        }

                        VsandTeamCustomCode oOppGroup = null/* TODO Change to default(_) if this is not a reference type */;
                        if (!bOutOfState)
                        {
                            if (bPrivate && bOppPrivate)
                            {
                                oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName.Equals("Group", System.StringComparison.OrdinalIgnoreCase));
                                if (oOppGroup == null)
                                {
                                    oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(cc => cc.CodeName.Equals("GroupExchange", System.StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            else
                            {
                                oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(cc => cc.CodeName.Equals("GroupExchange", System.StringComparison.OrdinalIgnoreCase));
                                if (oOppGroup == null)
                                {
                                    oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName.Equals("Group", System.StringComparison.OrdinalIgnoreCase));
                                }
                            }

                            if (oOppGroup != null)
                            {
                                sOppGroup = oOppGroup.CodeValue.ToLower();
                            }
                        }
                        else
                        {
                            oOppGroup = oOpp.Team.CustomCodes.FirstOrDefault(cc => cc.CodeName.Equals("GroupExchange", System.StringComparison.OrdinalIgnoreCase));
                            if (oOppGroup != null)
                            {
                                sOppGroup = oOppGroup.CodeValue;
                            }
                            VsandTeamCustomCode oOppWinEx = oOpp.Team.CustomCodes.FirstOrDefault(gt => gt.CodeName == "OOSFinalWins");
                            if (oOppWinEx != null)
                            {
                                string sOppWins = oOppWinEx.CodeValue;
                                int.TryParse(sOppWins, out iOppWins);
                            }
                        }
                        switch (sOppGroup.ToLower())
                        {
                            case "group 4":
                                {
                                    iGroupVal = 4;
                                    break;
                                }

                            case "group 3":
                                {
                                    iGroupVal = 3;
                                    break;
                                }

                            case "group 2":
                                {
                                    iGroupVal = 2;
                                    break;
                                }

                            case "group 1":
                                {
                                    iGroupVal = 1;
                                    break;
                                }
                        }

                        if (!bOutOfState)
                        {
                            List<VsandGameReport> oOppGameReports = null;

                            var oData = _uow.GameReports.List(
                                                            g => g.Teams.Any(gt => gt.Team.TeamId == iOppTeamId) && g.Deleted == false && g.Final == true && g.PPEligible == true && g.Teams.Count == 2 && g.GameDate >= this.StartDate && g.GameDate <= this.EndDate,
                                                            x => x.OrderBy(g => g.GameDate),
                                                            new List<string> { "Teams" }
                                                        ).Result.Take(this.IncludeGames);

                            if (oData != null)
                            {
                                oOppGameReports = oData.ToList();
                            }

                            foreach (VsandGameReport oOppGR in oOppGameReports)
                            {
                                var oOppGameTeams = oOppGR.Teams;
                                VsandGameReportTeam oOppTeam = oOppGameTeams.FirstOrDefault(gt => gt.TeamId == iOppTeamId);
                                double dOppTeamScore = oOppTeam.FinalScore;

                                VsandGameReportTeam oOppOpp = oOppGameTeams.FirstOrDefault(gt => gt.TeamId != iOppTeamId);
                                if (oOppOpp.TeamId != this.TeamId)
                                {
                                    double dOppOppScore = oOppOpp.FinalScore;

                                    if (dOppTeamScore > dOppOppScore)
                                    {
                                        iOppWins = iOppWins + 1;
                                    }
                                }
                            }
                        }
                    }

                    dPPTB = dPPTB + iGroupVal + iOppWins;
                }
            }

            return dPPTB;
        }

        public System.TypeCode GetTypeCode()
        {
            return TypeCode.Object;
        }

        public bool ToBoolean(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public byte ToByte(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public char ToChar(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public DateTime ToDateTime(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public decimal ToDecimal(System.IFormatProvider provider)
        {
            return this.ToDecimal();
        }

        public double ToDouble(System.IFormatProvider provider)
        {
            return (double)this.ToDecimal();
        }

        public short ToInt16(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public int ToInt32(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public long ToInt64(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public sbyte ToSByte(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public float ToSingle(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public string ToString(System.IFormatProvider provider)
        {
            return this.ToDecimal().ToString();
        }

        public object ToType(System.Type conversionType, System.IFormatProvider provider)
        {
            return this.GetType();
        }

        public ushort ToUInt16(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public uint ToUInt32(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }

        public ulong ToUInt64(System.IFormatProvider provider)
        {
            throw new InvalidCastException("NJAM.PowerPoints.LacrossePowerPoints cannot be converted to Boolean");
        }
    }
}
