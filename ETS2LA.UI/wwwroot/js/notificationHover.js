window.initNotificationHover = function () {
    if (window.notificationHoverHandler) return;

    let framePending = false;

    const updateOpacity = (event) => {
        if (framePending) return;

        framePending = true;
        requestAnimationFrame(() => {
            const targets = document.querySelectorAll(
                '[data-notification-hover-target]'
            );

            targets.forEach((target) => {
                const rect = target.getBoundingClientRect();
                const hovered =
                    event.clientX >= rect.left &&
                    event.clientX <= rect.right &&
                    event.clientY >= rect.top &&
                    event.clientY <= rect.bottom;

                target.style.opacity = hovered ? '0.0' : '1';
            });

            framePending = false;
        });
    };

    document.addEventListener('pointermove', updateOpacity, {
        passive: true
    });

    window.notificationHoverHandler = updateOpacity;
};