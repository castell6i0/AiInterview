(() => {
    'use strict';
    // One canvas, three looks: 'sphere' (signed-in: rotating globe of "Hello World"), 'matrix' (account pages: falling 0/1),
    // 'ascii' (public pages: drifting IT tokens). The header "Hiệu ứng" button is the on/off switch (html.motion-off = off).
    const canvas = document.getElementById('asciiBg');
    const toggle = document.getElementById('bgToggle');
    const mode = document.body.dataset.bg;
    if (!canvas || !['sphere', 'matrix', 'ascii'].includes(mode)) { canvas?.remove(); return; }
    const root = document.documentElement;
    const ctx = canvas.getContext('2d');
    const css = getComputedStyle(root);
    const accent = css.getPropertyValue('--accent').trim(), text = css.getPropertyValue('--text').trim(), font = css.getPropertyValue('--font-mono').trim();
    let width = 0, height = 0, raf = 0, last = 0, resizeTimer = 0, scene = null;
    const enabled = () => !root.classList.contains('motion-off');

    // Rotating sphere: latitude rings of characters; each ring reads "Hello World " along the equator direction.
    function sphereScene() {
        const word = 'Hello World ', rings = width <= 767 ? 13 : 19, perRing = width <= 767 ? 30 : 46, points = [];
        for (let i = 1; i < rings; i++) {
            const lat = -Math.PI / 2 + Math.PI * i / rings, count = Math.max(6, Math.round(perRing * Math.cos(lat)));
            for (let j = 0; j < count; j++) points.push({ lat, lon: 2 * Math.PI * j / count, ch: word[j % word.length] });
        }
        let angle = 0;
        const tilt = 0.38, cosT = Math.cos(tilt), sinT = Math.sin(tilt);
        return {
            step(seconds) { angle -= seconds * 0.22; },
            draw() {
                const radius = Math.min(width, height) * (width <= 767 ? 0.44 : 0.42);
                const cx = width <= 991 ? width / 2 : width * 0.62, cy = height / 2;
                ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillStyle = accent;
                for (const p of points) {
                    const c = Math.cos(p.lat), x = c * Math.sin(p.lon + angle), y = Math.sin(p.lat), z = c * Math.cos(p.lon + angle);
                    const y2 = y * cosT - z * sinT, z2 = y * sinT + z * cosT, depth = (z2 + 1) / 2; // 0 back .. 1 front
                    ctx.globalAlpha = 0.05 + 0.28 * depth * depth;
                    ctx.font = `${Math.round(9 + 7 * depth)}px ${font}`;
                    ctx.fillText(p.ch, cx + x * radius, cy + y2 * radius);
                }
            }
        };
    }
    // Falling 0/1 columns with a fading trail; the head character is brightest.
    function matrixScene() {
        const size = width <= 767 ? 14 : 16, rows = Math.ceil(height / size) + 1;
        const columns = Array.from({ length: Math.ceil(width / size) }, () => spawn(true));
        function spawn(initial) { return { y: initial ? Math.random() * rows : -Math.random() * rows * 0.5, speed: 6 + Math.random() * 10, length: 8 + Math.random() * 18 | 0, bits: Array.from({ length: 32 }, () => Math.random() < 0.5 ? '0' : '1') }; }
        return {
            step(seconds) {
                for (const col of columns) {
                    col.y += col.speed * seconds;
                    if (Math.random() < seconds * 3) col.bits[Math.random() * col.bits.length | 0] = Math.random() < 0.5 ? '0' : '1';
                    if (col.y - col.length > rows) Object.assign(col, spawn(false));
                }
            },
            draw() {
                ctx.font = `${size}px ${font}`; ctx.textAlign = 'center'; ctx.textBaseline = 'top';
                columns.forEach((col, index) => {
                    const head = Math.floor(col.y);
                    for (let k = 0; k < col.length; k++) {
                        const row = head - k;
                        if (row < 0 || row > rows) continue;
                        ctx.fillStyle = k === 0 ? text : accent;
                        ctx.globalAlpha = k === 0 ? 0.55 : 0.32 * (1 - k / col.length);
                        ctx.fillText(col.bits[(row % 32 + 32) % 32], index * size + size / 2, row * size);
                    }
                });
            }
        };
    }
    // Drifting IT tokens (public pages).
    function asciiScene() {
        const tokens = ['SELECT *', 'O(n log n)', 'git commit', 'async/await', '404', 'HashMap', '{}', '=>', '0x1F', 'try/catch',
            'LEFT JOIN', 'GROUP BY', 'O(1)', 'git rebase', '200 OK', 'null', 'yield', 'queue.pop()', 'mutex', 'TCP/IP', 'ACID', 'git diff', '[]', '&&', 'REST', 'index'];
        const spawn = y => ({ x: Math.random() * width, y, speed: 6 + Math.random() * 10, text: tokens[Math.random() * tokens.length | 0], alpha: 0.4 + Math.random() * 0.6 });
        const items = Array.from({ length: Math.round(width * height / (width <= 767 ? 40000 : 18000)) }, () => spawn(Math.random() * height));
        return {
            step(seconds) { for (const item of items) { item.y -= item.speed * seconds; if (item.y < -16) Object.assign(item, spawn(height + 16)); } },
            draw() {
                ctx.font = `13px ${font}`; ctx.textAlign = 'left'; ctx.textBaseline = 'alphabetic'; ctx.fillStyle = accent;
                for (const item of items) { ctx.globalAlpha = 0.14 * item.alpha; ctx.fillText(item.text, item.x, item.y); }
            }
        };
    }

    function draw() { ctx.clearRect(0, 0, width, height); scene.draw(); }
    function resize() {
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        width = window.innerWidth; height = window.innerHeight;
        canvas.width = Math.round(width * dpr); canvas.height = Math.round(height * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        scene = mode === 'sphere' ? sphereScene() : mode === 'matrix' ? matrixScene() : asciiScene();
    }
    function frame(now) {
        raf = requestAnimationFrame(frame);
        if (now - last < 33) return; // ~30fps cap
        const seconds = Math.min(now - last, 100) / 1000; last = now;
        scene.step(seconds); draw();
    }
    function stop() { cancelAnimationFrame(raf); raf = 0; }
    function start() {
        stop();
        canvas.hidden = !enabled();
        if (toggle) { toggle.hidden = false; toggle.setAttribute('aria-pressed', String(enabled())); }
        if (!enabled()) return;
        draw();
        if (!document.hidden) raf = requestAnimationFrame(now => { last = now; frame(now); });
    }

    toggle?.addEventListener('click', () => {
        const off = root.classList.toggle('motion-off');
        try { localStorage.setItem('effects', off ? 'off' : 'on'); } catch { /* preference not saved */ }
        start();
    });
    window.addEventListener('resize', () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(() => { resize(); start(); }, 150); });
    document.addEventListener('visibilitychange', () => document.hidden ? stop() : start());
    resize();
    start();
})();
