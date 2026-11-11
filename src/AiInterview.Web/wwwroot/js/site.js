'use strict';
document.getElementById('menuToggle')?.addEventListener('click',function(){const open=document.getElementById('appNav').classList.toggle('open');this.setAttribute('aria-expanded',String(open));});
document.addEventListener('click',e=>{const nav=document.getElementById('appNav');if(nav?.classList.contains('open')&&!nav.contains(e.target)&&!e.target.closest('#menuToggle')){nav.classList.remove('open');document.getElementById('menuToggle')?.setAttribute('aria-expanded','false');}});
document.querySelectorAll('form[data-confirm]').forEach(form=>form.addEventListener('submit',e=>{if(!window.confirm(form.dataset.confirm))e.preventDefault();}));
