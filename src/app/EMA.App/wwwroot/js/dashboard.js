// Dashboard JS interop — Chart.js rendering plus print / mailto / CSV helpers.
// Called from Pages/Dashboard/Dashboard.razor via IJSRuntime.InvokeVoidAsync.
// Requires Chart.js to be loaded first (see index.html).

(function () {
    "use strict";

    // Brand palette reused across the three charts.
    const PALETTE = [
        "rgb(5, 39, 103)", "#3a0647", "#1f7a8c", "#bfdbf7",
        "#e1bc29", "#8cb369", "#f4a259", "#bc4749",
        "#5b8e7d", "#9a8c98", "#4a5759", "#c08552"
    ];

    // Format a number as a currency-ish axis/tooltip label.
    const money = (value) => "$" + Number(value).toLocaleString("en-CA", { maximumFractionDigits: 0 });

    // Destroy any Chart instance already attached to the canvas before drawing a new one.
    // The Razor component recreates the <canvas> via @key on refresh, but guarding here keeps
    // Chart.js from throwing "Canvas is already in use" if the element id is ever reused.
    function renderChart(canvasId, config) {
        if (typeof Chart === "undefined") {
            console.error("Chart.js is not loaded; cannot render " + canvasId + ".");
            return;
        }

        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const existing = Chart.getChart(canvas);
        if (existing) existing.destroy();

        new Chart(canvas, config);
    }

    // labels may be two-line arrays like ["Jan", "2024"] so the year renders
    // below the month name on the x-axis. isCount switches the dataset from dollar
    // amounts ("Value") to raw transaction counts ("Number") — used by the EMA
    // Insiders page's Number/Value toggle; omitted/false everywhere else.
    window.initTrendChart = function (labels, amounts, isCount) {
        const count = (value) => Number(value).toLocaleString("en-CA");
        const fmt = isCount ? count : money;
        renderChart("trendChart", {
            type: "line",
            data: {
                labels: labels,
                datasets: [{
                    label: isCount ? "Number" : "Value",
                    data: amounts,
                    borderColor: PALETTE[0],
                    backgroundColor: "rgba(5, 39, 103, 0.1)",
                    fill: true,
                    tension: 0.3,
                    pointRadius: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: { callbacks: { label: (ctx) => fmt(ctx.parsed.y) } }
                },
                scales: {
                    x: { ticks: { autoSkip: true, maxRotation: 0, font: { size: 10 } } },
                    y: { beginAtZero: true, ticks: { callback: (v) => fmt(v) } }
                }
            }
        });
    };

    window.initFundChart = function (labels, amounts) {
        renderChart("fundChart", {
            type: "bar",
            data: {
                labels: labels,
                datasets: [{
                    label: "Value",
                    data: amounts,
                    backgroundColor: labels.map((_, i) => PALETTE[i % PALETTE.length]),
                    maxBarThickness: 90
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: { callbacks: { label: (ctx) => money(ctx.parsed.y) } }
                },
                scales: {
                    y: { beginAtZero: true, ticks: { callback: (v) => money(v) } }
                }
            }
        });
    };

    window.initMethodChart = function (labels, amounts) {
        renderChart("methodChart", {
            type: "bar",
            data: {
                labels: labels,
                datasets: [{
                    label: "Value",
                    data: amounts,
                    backgroundColor: labels.map((_, i) => PALETTE[i % PALETTE.length]),
                    maxBarThickness: 90
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: { callbacks: { label: (ctx) => money(ctx.parsed.y) } }
                },
                scales: {
                    y: { beginAtZero: true, ticks: { callback: (v) => money(v) } }
                }
            }
        });
    };

    // Prints only the report data (not the whole page). Adds a body class so the
    // @media print rules in app.css hide everything except #report-print-area, then
    // removes it once printing finishes.
    window.printPage = function () {
        document.body.classList.add("printing-report");

        var didCleanup = false;
        var cleanup = function () {
            if (didCleanup) return;
            didCleanup = true;
            document.body.classList.remove("printing-report");
            window.removeEventListener("afterprint", cleanup);
        };

        window.addEventListener("afterprint", cleanup);
        // Browsers that skip afterprint still refocus this tab after the print dialog.
        window.addEventListener("focus", function () { setTimeout(cleanup, 0); }, { once: true });

        setTimeout(function () { window.print(); }, 0);
    };

    // Like printPage, but sets a filename-friendly document title first so the browser's
    // "Save as PDF" uses it as the default file name; the title is restored afterward.
    window.savePdf = function (fileName) {
        var originalTitle = document.title;
        if (fileName && fileName.trim()) document.title = fileName.trim();
        document.body.classList.add("printing-report");

        var didCleanup = false;
        var cleanup = function () {
            if (didCleanup) return;
            didCleanup = true;
            document.body.classList.remove("printing-report");
            document.title = originalTitle;
            window.removeEventListener("afterprint", cleanup);
        };

        window.addEventListener("afterprint", cleanup);
        window.addEventListener("focus", function () { setTimeout(cleanup, 0); }, { once: true });

        setTimeout(function () { window.print(); }, 0);
    };

    // Navigates to a mailto: URL to open the user's email client (Email button).
    window.openMailto = function (url) {
        window.location.href = url;
    };

    // Triggers a client-side CSV download (Export CSV button).
    window.downloadCsv = function (filename, csv) {
        const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    };
})();

