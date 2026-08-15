// The only browser behaviour this site needs that markup cannot express. Kept to one module so a
// reader can see the whole of the site's scripting at once.

/**
 * Brings an element to the top of the viewport when it is not already near there.
 *
 * Selecting a measure updates a chart that can sit a screen below the picker, and a change nobody
 * sees is a change that did not happen. The test is on where the element starts rather than on how
 * much of it shows: these wrappers are taller than the viewport, so "enough of it is visible"
 * counted a heading as the chart and left the plot below the fold.
 */
export function revealIfOffScreen(element) {
    if (!element) {
        return;
    }

    const top = element.getBoundingClientRect().top;
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight;

    // Already at or near the top of the screen: scrolling under someone who can see the thing they
    // asked for is its own kind of wrong.
    if (top >= 0 && top <= viewportHeight * 0.35) {
        return;
    }

    element.scrollIntoView({
        // Respects the reader's motion preference rather than overriding it.
        behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
        block: 'start',
    });
}
