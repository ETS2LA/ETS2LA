// Hide the line in sync with the scrollbar
const scrollbarFadeDuration = 1000;
const scrollbarHideDelay = 2000;

window.initScrollDivider = function (scrollArea, divider) {
    let isScrollable = false;
    let opacity = 0, source = 0, target = 0;
    let startTime = 0, endTime = 0;
    let frame = 0, timer = 0;

    const render = () => {
        divider.style.borderTopColor = `color-mix(in srgb, var(--color-border) ${opacity * 100}%, transparent)`;
    };

    const stop = () => {
        cancelAnimationFrame(frame);
        clearTimeout(timer);
        frame = timer = 0;
    };

    const tick = () => {
        frame = 0;
        const now = performance.now();
        if (now < endTime) {
            const p = (now - startTime) / (endTime - startTime) - 1;
            opacity = source + (p * p * p + 1) * (target - source);
        } else {
            opacity = target;
        }
        render();

        if (opacity !== target) frame = requestAnimationFrame(tick);
        else hide();
    };

    function show() {
        if (!isScrollable) return;
        if ((frame || timer) && target === 1) return;
        stop();

        source = opacity;
        target = 1;
        if (opacity === 1) {
            hide();
            return;
        }
        startTime = performance.now();
        endTime = startTime + scrollbarFadeDuration;
        frame = requestAnimationFrame(tick);
    }

    function hide() {
        if ((frame || timer) && target === 0) return;
        stop();

        source = opacity;
        target = 0;
        if (opacity === 0) return;
        startTime = performance.now() + scrollbarHideDelay;
        endTime = startTime + scrollbarFadeDuration + scrollbarHideDelay;
        timer = setTimeout(() => {
            timer = 0;
            tick();
        }, scrollbarHideDelay);
    }

    const updateScrollable = () => {
        // Ignore 1px rounding differences from scaling
        const scrollable = scrollArea.scrollHeight - scrollArea.clientHeight > 1;
        if (scrollable === isScrollable) return;
        isScrollable = scrollable;

        stop();
        opacity = isScrollable ? 1 : 0;
        render();
        if (isScrollable) hide();
    };

    scrollArea.addEventListener('mouseenter', show);
    scrollArea.addEventListener('mousemove', show);
    scrollArea.addEventListener('mouseleave', hide);
    scrollArea.addEventListener('scroll', show);

    const observer = new ResizeObserver(updateScrollable);
    observer.observe(scrollArea);
    for (const child of scrollArea.children) observer.observe(child);
    render();
    updateScrollable();
};

window.scrollElementIntoView = function (element) {
    element?.scrollIntoView({ behavior: 'auto', block: 'end' });
};
