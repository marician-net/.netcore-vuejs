﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace VSAND.Backend.Areas.Reports.Controllers
{
    [Area("Reports")]
    public class DataQualityController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}