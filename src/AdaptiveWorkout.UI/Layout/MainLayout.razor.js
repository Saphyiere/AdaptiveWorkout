// The theme in use: the page's look follows data-theme on <html>, and index.html restores it from local storage before the first paint.
export function showTheme(id) {
    document.documentElement.dataset.theme = id;
    try {
        localStorage.setItem("theme", id);
    } catch {
        // No storage: the next start shows the default theme until the saved one loads.
    }
}
