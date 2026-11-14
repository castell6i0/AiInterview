'use strict';
document.getElementById('menuToggle')?.addEventListener('click',function(){const open=document.getElementById('appNav').classList.toggle('open');this.setAttribute('aria-expanded',String(open));});
document.addEventListener('click',e=>{const nav=document.getElementById('appNav');if(nav?.classList.contains('open')&&!nav.contains(e.target)&&!e.target.closest('#menuToggle')){nav.classList.remove('open');document.getElementById('menuToggle')?.setAttribute('aria-expanded','false');}});
document.querySelectorAll('form[data-confirm]').forEach(form=>form.addEventListener('submit',e=>{if(!window.confirm(form.dataset.confirm))e.preventDefault();}));
// Brand button: on desktop collapses the sidebar to an icon rail (remembered); on mobile it closes the drawer.
(()=>{const toggle=document.getElementById('sidebarToggle');if(!toggle)return;const root=document.documentElement;
const sync=()=>toggle.setAttribute('aria-expanded',String(!root.classList.contains('sidebar-collapsed')));sync();
toggle.addEventListener('click',()=>{if(window.matchMedia('(max-width: 767px)').matches){document.getElementById('appNav')?.classList.remove('open');document.getElementById('menuToggle')?.setAttribute('aria-expanded','false');return;}
const collapsed=root.classList.toggle('sidebar-collapsed');sync();try{localStorage.setItem('sidebar',collapsed?'collapsed':'open');}catch{}});})();
// Public top bar: show its solid background once the page is scrolled.
(()=>{const bar=document.getElementById('publicBar');if(!bar)return;const update=()=>bar.classList.toggle('is-scrolled',window.scrollY>8);update();window.addEventListener('scroll',update,{passive:true});})();
// Click ripple on buttons, from the pointer position (skipped when effects are off).
document.addEventListener('pointerdown',e=>{const b=e.target.closest?.('.btn');if(!b||b.disabled||document.documentElement.classList.contains('motion-off'))return;const r=b.getBoundingClientRect(),size=Math.max(r.width,r.height)*2.2,dot=document.createElement('span');dot.className='ripple';dot.style.cssText=`width:${size}px;height:${size}px;left:${e.clientX-r.left-size/2}px;top:${e.clientY-r.top-size/2}px`;dot.addEventListener('animationend',()=>dot.remove());b.append(dot);});
// Copy buttons (e.g. class join code): [data-copy] puts its value on the clipboard and confirms in place.
document.addEventListener('click',async e=>{
    const button=e.target.closest('[data-copy]');
    if(!button)return;
    const label=button.querySelector('span');
    const done=text=>{if(!label)return;const old=label.dataset.idle??label.textContent;label.dataset.idle=old;label.textContent=text;button.classList.add('is-copied');clearTimeout(button._copyTimer);button._copyTimer=setTimeout(()=>{label.textContent=old;button.classList.remove('is-copied');},2000);};
    try{await navigator.clipboard.writeText(button.dataset.copy);done('Đã sao chép');}
    catch{const area=Object.assign(document.createElement('textarea'),{value:button.dataset.copy});area.setAttribute('readonly','');area.style.position='fixed';area.style.opacity='0';document.body.append(area);area.select();const ok=document.execCommand('copy');area.remove();done(ok?'Đã sao chép':'Không sao chép được');}
});

// Popovers (.glass-pop) are native; CSS anchor positioning places them. Where that is unsupported, place them here instead.
(() => {
    if (CSS.supports('anchor-name: --a')) return;
    const place = pop => {
        const anchor = document.querySelector(`[popovertarget="${pop.id}"]`) || document.getElementById(pop.dataset.anchor || '');
        if (!anchor) return;
        const r = anchor.getBoundingClientRect(), w = pop.offsetWidth, h = pop.offsetHeight;
        let left, top;
        if (pop.classList.contains('row-menu')) { left = r.left - w - 8; if (left < 8) left = r.right + 8; top = r.top + r.height / 2 - h / 2; }
        else { left = Math.min(r.left, innerWidth - w - 8); top = r.bottom + 8; }
        Object.assign(pop.style, { position: 'fixed', left: `${Math.max(8, left)}px`, top: `${Math.max(8, top)}px` });
    };
    document.addEventListener('toggle', e => { if (e.newState === 'open' && e.target.classList?.contains('glass-pop')) place(e.target); }, true);
    addEventListener('scroll', () => document.querySelectorAll('.glass-pop:popover-open').forEach(place), { passive: true });
})();

// Role / lock forms: changing either opens a small confirm popover with Lưu; Hủy (or Escape) restores the old values.
(() => {
    document.querySelectorAll('[data-access-form]').forEach(form => {
        const role = form.querySelector('select[name="Role"]');
        const lock = form.querySelector('input[name="Locked"]');
        const pop = form.querySelector('.access-confirm');
        const text = form.querySelector('.access-confirm-text');
        if (!pop || !pop.showPopover) return; // no Popover API: the inline Lưu button stays visible
        pop.setAttribute('popover', 'manual');
        pop.classList.add('glass-pop');
        const initialRole = role?.value, initialLock = lock?.checked;
        const update = () => {
            const roleChanged = role && role.value !== initialRole, lockChanged = lock && lock.checked !== initialLock;
            if (text) text.textContent = roleChanged && lockChanged ? 'Bạn vừa đổi quyền và trạng thái khóa của người dùng này. Bạn có đồng ý lưu?'
                : lockChanged ? 'Bạn vừa đổi trạng thái khóa của người dùng này. Bạn có đồng ý lưu?'
                : 'Bạn vừa đổi quyền của người dùng này. Bạn có đồng ý lưu?';
            const open = pop.matches(':popover-open');
            if ((roleChanged || lockChanged) && !open) pop.showPopover();
            if (!(roleChanged || lockChanged) && open) pop.hidePopover();
        };
        const cancel = () => { if (role) role.value = initialRole; if (lock) lock.checked = initialLock; update(); role?.focus(); };
        role?.addEventListener('change', update);
        lock?.addEventListener('change', update);
        form.querySelector('[data-access-cancel]')?.addEventListener('click', cancel);
        form.addEventListener('keydown', e => { if (e.key === 'Escape' && pop.matches(':popover-open')) cancel(); });
    });
})();
