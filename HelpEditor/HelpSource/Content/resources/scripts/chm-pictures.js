// The compiled help (CHM) viewer draws text at the display's scaling but pictures at their raw pixel size, so on a scaled display the charts and equations
// come out small beside the text: at 250% a chart meant to be 566 pixels wide is drawn 566 device pixels wide, two fifths of its intended size.
// In that viewer an inch is scaled where a pixel is not, so measure an inch, and enlarge the pictures by the same factor.  Anywhere pixels are already scaled
// (every ordinary browser, and a display at 100%) the factor is 1 and nothing is done.
(function () {
    var factor = 1;

    function measure() {
        var probe = document.createElement("div");
        probe.style.cssText = "position:absolute;left:-10in;top:0;width:1in;height:1in;padding:0;border:0;visibility:hidden";
        document.body.appendChild(probe);
        var inch = probe.offsetWidth;
        document.body.removeChild(probe);
        return inch > 0 ? inch / 96 : 1;
    }

    function fit() {
        var page = document.documentElement.clientWidth || document.body.clientWidth;
        var scrolled = document.documentElement.scrollLeft || document.body.scrollLeft || 0;
        // The stylesheet keeps 1em of the 10pt text clear at the right of the page, and that margin grows with the display scaling as the text does.
        var margin = Math.round(16 * factor);
        var pictures = document.getElementsByTagName("img");
        for (var i = 0; i < pictures.length; i++) {
            var picture = pictures[i];
            // Remember each picture's authored size the first time, so that resizing the window starts from it again.
            if (!picture.sdWidth) {
                if (!picture.offsetWidth || !picture.offsetHeight)
                    continue;
                picture.sdWidth = picture.offsetWidth;
                picture.sdHeight = picture.offsetHeight;
            }
            picture.style.width = Math.round(picture.sdWidth * factor) + "px";
            picture.style.height = Math.round(picture.sdHeight * factor) + "px";
            // Too wide for the window?  How much room a picture has depends on where it starts (one in an indented paragraph starts further right), and that
            // is known only once it has been laid out at full size: so ask now.  Never go below the authored size: the equation pictures carry two dots to the
            // pixel, and this viewer drops thin strokes (the bars of a small "=") from a picture it has to shrink by more than two to one.
            var room = page - (picture.getBoundingClientRect().left + scrolled) - margin;
            if (room > 100 && picture.sdWidth * factor > room) {
                var scale = Math.max(1, room / picture.sdWidth);
                picture.style.width = Math.round(picture.sdWidth * scale) + "px";
                picture.style.height = Math.round(picture.sdHeight * scale) + "px";
            }
        }
    }

    function start() {
        factor = measure();
        if (factor < 1.05)
            return;
        fit();
        if (window.attachEvent)
            window.attachEvent("onresize", fit);
        else
            window.addEventListener("resize", fit, false);
    }

    if (window.attachEvent)
        window.attachEvent("onload", start);
    else
        window.addEventListener("load", start, false);
})();
