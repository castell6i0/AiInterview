(() => {
    'use strict';
    // Admin overview: each chart animates when it scrolls into view (line draws point by point, bars rise one after
    // another, doughnut sweeps round) and KPI numbers count up. Everything renders at once with the "Hiệu ứng" switch off.
    const source = document.getElementById('adminChartData');
    if (!source || !window.Chart || !window.ChartTheme) return;
    const data = JSON.parse(source.textContent);
    const t = ChartTheme.token;
    const still = () => document.documentElement.classList.contains('motion-off');
    const intAxis = { beginAtZero: true, ticks: { precision: 0 } };
    const base = { maintainAspectRatio: false, plugins: { legend: { display: false } } };

    // Progressive line: every point starts when the previous one has landed.
    function lineAnimations(count) {
        const step = 1600 / Math.max(1, count);
        const previousY = ctx => ctx.index === 0 ? ctx.chart.scales.y.getPixelForValue(0)
            : ctx.chart.getDatasetMeta(ctx.datasetIndex).data[ctx.index - 1].getProps(['y'], true).y;
        const delay = key => ctx => { if (ctx.type !== 'data' || ctx[key]) return 0; ctx[key] = true; return ctx.index * step; };
        return {
            x: { type: 'number', easing: 'linear', duration: step, from: NaN, delay: delay('xStarted') },
            y: { type: 'number', easing: 'linear', duration: step, from: previousY, delay: delay('yStarted') }
        };
    }
    const staggered = { duration: 900, easing: 'easeOutQuart', delay: ctx => ctx.type === 'data' && ctx.mode === 'default' ? ctx.dataIndex * 90 : 0 };

    const charts = {
        sessionsChart: () => ({
            type: 'line',
            data: { labels: data.days, datasets: [{ label: 'Buổi luyện', data: data.sessions, borderColor: t('--accent'), backgroundColor: ChartTheme.fill('--accent'), fill: true, tension: .35, cubicInterpolationMode: 'monotone', pointRadius: 3, borderWidth: 2 }] },
            options: { ...base, scales: { y: intAxis }, ...(still() ? { animation: false } : { animations: lineAnimations(data.sessions.length) }) }
        }),
        aiChart: () => ({
            type: 'bar',
            data: { labels: data.days, datasets: [{ label: 'Lượt gọi AI', data: data.ai, backgroundColor: t('--cyan'), borderRadius: 3 }] },
            options: { ...base, scales: { y: intAxis }, animation: still() ? false : staggered }
        }),
        rolesChart: () => ({
            type: 'doughnut',
            data: { labels: ['Sinh viên', 'Giảng viên', 'Quản trị'], datasets: [{ data: data.roles, backgroundColor: [t('--accent'), t('--cyan'), t('--warn')], borderColor: t('--surface-1'), borderWidth: 3 }] },
            options: { ...base, cutout: '62%', animation: still() ? false : { duration: 1400, easing: 'easeOutQuart', animateRotate: true, animateScale: true }, plugins: { legend: { display: true, position: 'right' } } }
        }),
        scoresChart: () => ({
            type: 'bar',
            data: { labels: ['Cần cải thiện', 'Khá', 'Tốt', 'Xuất sắc'], datasets: [{ label: 'Số buổi', data: data.bands, backgroundColor: [t('--danger'), t('--warn'), t('--cyan'), t('--accent')], borderRadius: 3 }] },
            options: { ...base, scales: { y: intAxis }, animation: still() ? false : { ...staggered, delay: ctx => ctx.type === 'data' && ctx.mode === 'default' ? ctx.dataIndex * 180 : 0 } }
        })
    };

    function countUp(el) {
        const target = Number(el.dataset.count);
        if (!Number.isFinite(target) || target === 0 || still()) return;
        const start = performance.now(), duration = 1200;
        const tick = now => {
            const p = Math.min(1, (now - start) / duration), eased = 1 - Math.pow(1 - p, 4);
            el.textContent = Math.round(target * eased).toLocaleString('vi-VN');
            if (p < 1) requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
    }

    const start = el => {
        if (el.dataset.started) return;
        el.dataset.started = '1';
        if (el.tagName === 'CANVAS') new Chart(el, charts[el.id]());
        else countUp(el);
    };
    const targets = [...Object.keys(charts).map(id => document.getElementById(id)).filter(Boolean), ...document.querySelectorAll('.kpi-value[data-count]')];
    // Start whatever is (mostly) on screen now, then on every scroll/resize until all have started.
    let pending = targets;
    const check = () => {
        const line = window.innerHeight * .85;
        pending = pending.filter(el => { const r = el.getBoundingClientRect(); if (r.top < line && r.bottom > 0) { start(el); return false; } return true; });
        if (pending.length === 0) { window.removeEventListener('scroll', check); window.removeEventListener('resize', check); }
    };
    window.addEventListener('scroll', check, { passive: true });
    window.addEventListener('resize', check);
    check();
})();
