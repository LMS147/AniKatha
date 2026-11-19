const positions = new Map();
let stop;

export function activate(heading, mediaType, restore) {
    if (!heading?.isConnected) return;
    stop?.();
    if (restore) window.scrollTo(0, positions.get(mediaType) ?? 0);
    else window.scrollTo(0, 0);

    const remember = () => {
        if (heading.isConnected) positions.set(mediaType, window.scrollY);
    };
    window.addEventListener("scroll", remember, { passive: true });
    stop = () => window.removeEventListener("scroll", remember);
}
