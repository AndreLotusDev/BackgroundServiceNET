// The parts of Fila's custom.js the app uses. custom.js itself is not loaded: it also wires up demo widgets
// (Swiper, Quill, calendars, ...) and throws when their libraries are missing. Dark only, so no theme toggles.
(() => {
    "use strict";

    // Preloader
    window.addEventListener("load", () => {
        const preloader = document.getElementById("preloader");
        if (preloader) {
            preloader.style.display = "none";
        }
    });

    // Sticky header
    const header = document.getElementById("header-area");
    if (header) {
        window.addEventListener("scroll", event => {
            header.classList.toggle("sticky", event.target.scrollingElement.scrollTop >= 150);
        });
    }

    // Sidebar menu (sidebar-menu.js)
    document.querySelectorAll("#layout-menu").forEach(element => {
        const menu = new Menu(element, { orientation: "vertical", closeChildren: false });
        // On resize, Menu.manageScroll swaps in PerfectScrollbar, which Fila does not ship (the menu scrolls with
        // simplebar via data-simplebar), and it throws on "undefined.destroy()". Nothing for it to do here.
        menu.manageScroll = () => { };
        window.Helpers.mainMenu = menu;
        window.Helpers.scrollToActive(false);
    });

    // Burger buttons: show / hide the sidebar (on a phone it is hidden until opened)
    const toggleSidebar = () => {
        const hidden = document.body.getAttribute("sidebar-data-theme") === "sidebar-hide";
        document.body.setAttribute("sidebar-data-theme", hidden ? "sidebar-show" : "sidebar-hide");
    };
    ["sidebar-burger-menu", "sidebar-burger-menu-close", "header-burger-menu"].forEach(id =>
        document.getElementById(id)?.addEventListener("click", toggleSidebar));

    // Bootstrap tooltips
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(element => new bootstrap.Tooltip(element));

    // Password show / hide
    document.querySelectorAll(".password-container").forEach(container => {
        const input = container.querySelector(".password");
        const icon = container.querySelector(".password-toggle-icon");
        icon?.addEventListener("click", () => {
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            icon.classList.toggle("ri-eye-line", show);
            icon.classList.toggle("ri-eye-off-line", !show);
        });
    });
})();
