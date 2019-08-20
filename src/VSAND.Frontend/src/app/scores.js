﻿// Import any components that you need to use, and make sure to expose them in the components section, too!
import Schedule from "./components/schedule-generic.vue";

// Give your app a unique name!
var scoresApp = new Vue({
    // The main element that app will attach itself to
    el: '#vueApp',

    components: {
        // Register components that are used
        "schedule": Schedule,
    },

    data: {
        // Reactive properties

    },

    created() {
        // Load whatever we need to get via ajax (not included in the Model)
        // Setup any non-reactive properties here
        // load the sport + game meta configuration
        var vm = this;

    },

    mounted() {
        // Anything that needs to take place after the app is mounted
        var vm = this;

    },

    computed: {

    },

    watch: {
        // Data and Computed properties to watch
    },

    methods: {

    }
});
