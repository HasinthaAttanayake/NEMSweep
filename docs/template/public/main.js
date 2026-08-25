/**
 * docfx modern template hooks. The template reads this default export at start-up.
 */
export default {
    // Follows the reader's system setting, which is also what the logo's own media query follows,
    // so the mark and the page agree unless the reader overrides the toggle.
    defaultTheme: "auto",

    // The app's chrome carries a GitHub link on every page; the docs had none.
    iconLinks: [
        {
            icon: "github",
            href: "https://github.com/HasinthaAttanayake/NEMSweep",
            title: "GitHub",
        },
    ],
};
