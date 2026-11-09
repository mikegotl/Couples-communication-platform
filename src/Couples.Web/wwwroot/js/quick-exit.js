// "Leave quickly": replace the current history entry with a neutral site so Back doesn't return.
document.addEventListener("click", function (e) {
    var link = e.target instanceof Element ? e.target.closest("[data-quick-exit]") : null;
    if (!link) {
        return;
    }

    e.preventDefault();
    window.location.replace(link.getAttribute("href"));
});

// "Skip to main content": move focus to the main landmark without a navigation.
document.addEventListener("click", function (e) {
    var link = e.target instanceof Element ? e.target.closest("[data-skip-link]") : null;
    var main = document.getElementById("main-content");
    if (!link || !main) {
        return;
    }

    e.preventDefault();
    main.focus();
    main.scrollIntoView();
});
