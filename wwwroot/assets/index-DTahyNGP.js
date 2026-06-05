(function(){let e=document.createElement(`link`).relList;if(e&&e.supports&&e.supports(`modulepreload`))return;for(let e of document.querySelectorAll(`link[rel="modulepreload"]`))n(e);new MutationObserver(e=>{for(let t of e)if(t.type===`childList`)for(let e of t.addedNodes)e.tagName===`LINK`&&e.rel===`modulepreload`&&n(e)}).observe(document,{childList:!0,subtree:!0});function t(e){let t={};return e.integrity&&(t.integrity=e.integrity),e.referrerPolicy&&(t.referrerPolicy=e.referrerPolicy),e.crossOrigin===`use-credentials`?t.credentials=`include`:e.crossOrigin===`anonymous`?t.credentials=`omit`:t.credentials=`same-origin`,t}function n(e){if(e.ep)return;e.ep=!0;let n=t(e);fetch(e.href,n)}})();var e=window.location.origin,t=13,n=24,r=[],i=[],a=``,o=document.querySelector(`#budgetSearchInput`),s=document.querySelector(`#budgetsList`),c=document.querySelector(`#refreshBudgetsButton`),l=document.querySelector(`#resetAllButton`),u=document.querySelector(`#waitingList`),d=document.querySelector(`#refreshWaitingButton`),f=document.querySelector(`#canceledList`),p=document.querySelector(`#refreshCanceledButton`);async function m(){try{let t=await fetch(`${e}/budgets`);if(!t.ok){s.innerHTML=`<p class="empty">Erro ao carregar orçamentos.</p>`;return}r=await t.json(),y(),_()}catch(e){console.error(`Erro ao carregar orçamentos:`,e),s.innerHTML=`<p class="empty">Erro de conexão com a API.</p>`}}async function h(){try{let t=await fetch(`${e}/conversations`);if(!t.ok){u.innerHTML=`<p class="empty">Erro ao carregar clientes em espera.</p>`;return}i=await t.json(),g()}catch(e){console.error(`Erro ao carregar clientes em espera:`,e),u.innerHTML=`<p class="empty">Erro de conexão com a API.</p>`}}function g(){let e=i.filter(e=>e.stage===t||e.stage===n);if(e.length===0){u.innerHTML=`<p class="empty">Nenhum cliente em espera.</p>`;return}u.innerHTML=e.map(e=>{let t=e.stage===n,r=t?`<span class="status status-attended">Já foi atendido</span>`:`<span class="status status-waiting">Em espera</span>`,i=t?`<button class="revert-button" data-attend-phone="${e.phone}" data-attended="false">Cliente não atendido</button>`:`<button class="attend-button" data-attend-phone="${e.phone}" data-attended="true">Cliente atendido</button>`;return`
        <article class="budget-card${t?` attended`:``}">
          <div class="budget-header">
            <strong>${e.customerName??`Cliente`}</strong>
            ${r}
          </div>

          <p><b>Telefone:</b> ${e.phone}</p>
          <p><b>Produto:</b> ${e.product??`Não informado`}</p>
          <p><b>Quantidade:</b> ${e.quantity??`Não informado`}</p>
          <p><b>Atualizado em:</b> ${v(e.updatedAt)}</p>

          <div class="status-actions">
            ${i}
          </div>
        </article>
      `}).join(``)}function _(){let e=r.filter(e=>e.status.toLowerCase().includes(`cancelado`));if(e.length===0){f.innerHTML=`<p class="empty">Nenhum orçamento cancelado.</p>`;return}f.innerHTML=e.map(e=>`
        <article class="budget-card">
          <div class="budget-header">
            <strong>${e.customerName}</strong>
            <span class="status status-canceled">${e.status}</span>
          </div>

          <p><b>Telefone:</b> ${e.phone}</p>
          <p><b>Produto:</b> ${e.product}</p>
          <p><b>Quantidade:</b> ${e.quantity}</p>
          <p><b>Tipo:</b> ${e.deliveryType}</p>
          <p><b>Atualizado em:</b> ${v(e.updatedAt)}</p>
        </article>
      `).join(``)}function v(e){return new Date(e).toLocaleString(`pt-BR`)}function y(){let e=r.filter(e=>e.customerName.toLowerCase().includes(a.toLowerCase()));if(e.length===0){s.innerHTML=`<p class="empty">Nenhum orçamento encontrado.</p>`;return}s.innerHTML=e.map(e=>`
        <article class="budget-card">
          <div class="budget-header">
            <strong>${e.customerName}</strong>
            <span class="status">${e.status}</span>
          </div>

          <p><b>Telefone:</b> ${e.phone}</p>
          <p><b>Possui cadastro:</b> ${e.hasRegistration?`Sim`:`Não`}</p>
          <p><b>Produto:</b> ${e.product}</p>
          <p><b>Quantidade:</b> ${e.quantity}</p>
          <p><b>Tipo:</b> ${e.deliveryType}</p>

          <div class="status-actions">
            <button data-budget-id="${e.id}" data-status="Pendente">Pendente</button>
            <button data-budget-id="${e.id}" data-status="Em atendimento">Em atendimento</button>
            <button data-budget-id="${e.id}" data-status="Fechado">Fechado</button>
            <button data-budget-id="${e.id}" data-status="Perdido">Perdido</button>
            <button data-budget-id="${e.id}" data-status="Atendido">Cliente atendido</button>
          </div>
        </article>
      `).join(``)}async function b(t,n){try{if(!(await fetch(`${e}/budgets/${t}/status`,{method:`PATCH`,headers:{"Content-Type":`application/json`},body:JSON.stringify({status:n})})).ok){alert(`Erro ao atualizar status.`);return}await m()}catch(e){console.error(`Erro ao atualizar status:`,e),alert(`Erro de conexão com a API.`)}}async function x(t,n){try{if(!(await fetch(`${e}/conversations/${t}/attendance`,{method:`PATCH`,headers:{"Content-Type":`application/json`},body:JSON.stringify({attended:n})})).ok){alert(`Erro ao atualizar atendimento.`);return}await h()}catch(e){console.error(`Erro ao atualizar atendimento:`,e),alert(`Erro de conexão com a API.`)}}async function S(){if(confirm(`Deseja limpar todos os dados da POC?`))try{await fetch(`${e}/simulator/reset-all`,{method:`POST`}),await m(),await h()}catch(e){console.error(`Erro ao resetar POC:`,e),alert(`Erro de conexão com a API.`)}}c.addEventListener(`click`,m),d.addEventListener(`click`,h),p.addEventListener(`click`,m),l.addEventListener(`click`,S),s.addEventListener(`click`,async e=>{let t=e.target,n=t.dataset.budgetId,r=t.dataset.status;!n||!r||await b(n,r)}),u.addEventListener(`click`,async e=>{let t=e.target,n=t.dataset.attendPhone;n&&await x(n,t.dataset.attended===`true`)}),o.addEventListener(`input`,e=>{a=e.target.value.trim(),y()}),m(),h();