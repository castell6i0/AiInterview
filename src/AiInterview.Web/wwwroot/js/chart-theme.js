(() => {
    'use strict';
    if (!window.Chart) return;
    const css = getComputedStyle(document.documentElement);
    const token = name => css.getPropertyValue(name).trim();
    const mono = token('--font-mono');
    const d = Chart.defaults;
    d.color = token('--muted');
    d.borderColor = token('--border');
    d.font.family = mono;
    d.font.size = 12;
    d.plugins.legend.labels.color = token('--text');
    d.plugins.legend.labels.boxWidth = 12;
    Object.assign(d.plugins.tooltip, {
        backgroundColor: token('--surface-2'), borderColor: token('--border'), borderWidth: 1,
        titleColor: token('--text'), bodyColor: token('--text'), titleFont: { family: mono }, bodyFont: { family: mono }, padding: 10, cornerRadius: 6
    });
    d.scale.grid.color = token('--border');
    d.scale.ticks.color = token('--muted');
    // Radar uses a radial scale whose lines and point labels are configured separately.
    Object.assign(d.scales.radialLinear.grid, { color: token('--border') });
    Object.assign(d.scales.radialLinear.angleLines, { color: token('--border') });
    Object.assign(d.scales.radialLinear.pointLabels, { color: token('--text'), font: { family: mono, size: 12 } });
    d.scales.radialLinear.ticks.backdropColor = 'transparent';
    d.animation.duration = document.documentElement.classList.contains('motion-off') ? 0 : 400;
    // Series differ by colour and dash pattern, so lines stay distinguishable without colour.
    const palette = ['--accent', '--cyan', '--text', '--muted'].map(token);
    const dashes = [[], [6, 4], [2, 3], [10, 3, 2, 3]];
    window.ChartTheme = {
        token,
        fill: name => token(name) + '2e', // hex token + ~18% alpha
        series: i => ({ borderColor: palette[i % 4], backgroundColor: palette[i % 4], borderDash: dashes[Math.floor(i / 4) % 4], pointRadius: 3, borderWidth: 2 })
    };
})();
