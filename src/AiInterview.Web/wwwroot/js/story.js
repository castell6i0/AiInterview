(() => {
    'use strict';
    // Landing scrollytelling: the step in the middle of the viewport picks the scene shown in the sticky window;
    // the hero fades and lifts as it scrolls away. Without JS all scenes are listed under each other (CSS fallback).
    const story = document.querySelector('.story');
    if (!story) return;
    const steps = [...story.querySelectorAll('.story-step')];
    const scenes = [...story.querySelectorAll('.scene')];
    const dots = [...story.querySelectorAll('.story-progress i')];
    story.classList.add('is-ready');

    function activate(index) {
        steps.forEach((step, i) => step.classList.toggle('is-active', i === index));
        scenes.forEach((scene, i) => scene.classList.toggle('is-active', i === index));
        dots.forEach((dot, i) => { dot.classList.toggle('is-active', i === index); dot.classList.toggle('is-done', i < index); });
    }
    activate(0);
    const observer = new IntersectionObserver(entries => {
        for (const entry of entries) if (entry.isIntersecting) activate(steps.indexOf(entry.target));
    }, { rootMargin: '-45% 0px -45% 0px' });
    steps.forEach(step => observer.observe(step));

    const hero = document.getElementById('heroInner');
    if (!hero) return;
    let ticking = false;
    function update() {
        ticking = false;
        const progress = Math.min(1, Math.max(0, window.scrollY / Math.max(1, hero.parentElement.offsetHeight)));
        hero.style.setProperty('--p', progress.toFixed(3));
    }
    window.addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(update); } }, { passive: true });
    update();
})();
