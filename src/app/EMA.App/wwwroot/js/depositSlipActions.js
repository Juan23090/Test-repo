window.depositSlipActions = (function () {
    function runPrintMode(elementID, title) {
        var slip = document.getElementById(elementID);
        if (!slip) {
            throw new Error("Could not find deposit slip element to print.");
        }

        var originalTitle = document.title;
        document.body.classList.add("deposit-slip-print-mode");

        if (title && title.trim()) {
            document.title = title.trim();
        }

        var didCleanup = false;
        var cleanup = function () {
            if (didCleanup) {
                return;
            }

            didCleanup = true;
            document.body.classList.remove("deposit-slip-print-mode");
            document.title = originalTitle;
            window.removeEventListener("afterprint", cleanup);
        };

        window.addEventListener("afterprint", cleanup);
        window.addEventListener("focus", function () {
            // Browsers that skip afterprint still refocus this tab after the print dialog.
            setTimeout(cleanup, 0);
        }, { once: true });

        setTimeout(function () {
            window.print();
        }, 0);
    }

    function printPdf(elementID) {
        runPrintMode(elementID, "Deposit Slip");
    }

    function saveAsPdf(elementID, fileName) {
        var title = fileName && fileName.trim() ? fileName.trim() : "deposit-slip";
        runPrintMode(elementID, title);
    }

    return {
        printPdf: printPdf,
        saveAsPdf: saveAsPdf
    };
})();

