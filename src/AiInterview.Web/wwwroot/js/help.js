(() => {
    'use strict';
    // Help page: one guide card at a time. Prev/next buttons, step dots, ←/→ keys and #id links (e.g. /Home/Help#connectionHelp)
    // pick the slide. Without JS every slide is listed (CSS fallback on .guide:not(.is-ready)).
    const guide = document.getElementById('guide');
    if (!guide) return;
    const deck = document.getElementById('guideDeck');
    const slides = [...guide.querySelectorAll('.guide-slide')];
    const bars = [...guide.querySelectorAll('.guide-progress i')];
    const prev = document.getElementById('guidePrev');
    const next = document.getElementById('guideNext');
    const count = document.getElementById('guideCount');
    const tip = document.getElementById('coachTip');
    const coach = guide.querySelector('.guide-coach');
    const dotsBox = document.getElementById('guideDots');
    const pad = n => String(n).padStart(2, '0');
    let current = 0, timer = 0, cueTimer = 0;

    const dots = slides.map((slide, i) => {
        const dot = document.createElement('button');
        dot.type = 'button';
        dot.className = 'guide-dot';
        dot.setAttribute('aria-label', `Bước ${i + 1}: ${slide.querySelector('h2').textContent}`);
        dot.addEventListener('click', () => go(i));
        dotsBox.append(dot);
        return dot;
    });

    function render() {
        slides.forEach((slide, i) => {
            const on = i === current;
            slide.classList.toggle('is-active', on);
            slide.inert = !on;
            slide.setAttribute('aria-hidden', String(!on));
        });
        dots.forEach((dot, i) => { dot.classList.toggle('is-active', i === current); dot.classList.toggle('is-done', i < current); dot.toggleAttribute('aria-current', i === current); });
        bars.forEach((bar, i) => bar.classList.toggle('is-done', i <= current));
        count.textContent = `BƯỚC ${pad(current + 1)} / ${pad(slides.length)}`;
        prev.disabled = current === 0;
        next.disabled = current === slides.length - 1;
        if (tip) tip.textContent = slides[current].dataset.tip || '';
    }

    function go(index) {
        if (index === current || index < 0 || index >= slides.length) return;
        const leaving = slides[current];
        deck.dataset.dir = index > current ? 'next' : 'prev';
        leaving.classList.add('is-leaving');
        current = index;
        render();
        // Restart the scan line and the coach's pointing cue.
        deck.classList.remove('is-switching'); void deck.offsetWidth; deck.classList.add('is-switching');
        coach?.classList.remove('is-cue'); void coach?.offsetWidth; coach?.classList.add('is-cue');
        clearTimeout(timer); clearTimeout(cueTimer);
        timer = setTimeout(() => { slides.forEach(s => s.classList.remove('is-leaving')); deck.classList.remove('is-switching'); }, 650);
        cueTimer = setTimeout(() => coach?.classList.remove('is-cue'), 1000);
        try { history.replaceState(null, '', `#${slides[current].querySelector('h2').id}`); } catch { /* hash not updated */ }
    }

    prev.addEventListener('click', () => go(current - 1));
    next.addEventListener('click', () => go(current + 1));
    deck.addEventListener('keydown', event => {
        if (event.target.closest('input,textarea,select,summary')) return;
        if (event.key === 'ArrowRight') { event.preventDefault(); go(current + 1); }
        if (event.key === 'ArrowLeft') { event.preventDefault(); go(current - 1); }
    });
    deck.tabIndex = -1;

    function fromHash() {
        const target = location.hash && document.getElementById(decodeURIComponent(location.hash.slice(1)));
        const index = target ? slides.findIndex(slide => slide.contains(target)) : -1;
        return index;
    }
    const start = fromHash();
    if (start > 0) current = start;
    guide.classList.add('is-ready');
    render();
    window.addEventListener('hashchange', () => { const i = fromHash(); if (i >= 0) go(i); });
})();
