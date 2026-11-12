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
