(function(){let e=document.createElement(`link`).relList;if(e&&e.supports&&e.supports(`modulepreload`))return;for(let e of document.querySelectorAll(`link[rel="modulepreload"]`))n(e);new MutationObserver(e=>{for(let t of e)if(t.type===`childList`)for(let e of t.addedNodes)e.tagName===`LINK`&&e.rel===`modulepreload`&&n(e)}).observe(document,{childList:!0,subtree:!0});function t(e){let t={};return e.integrity&&(t.integrity=e.integrity),e.referrerPolicy&&(t.referrerPolicy=e.referrerPolicy),e.crossOrigin===`use-credentials`?t.credentials=`include`:e.crossOrigin===`anonymous`?t.credentials=`omit`:t.credentials=`same-origin`,t}function n(e){if(e.ep)return;e.ep=!0;let n=t(e);fetch(e.href,n)}})();var e=window.location.origin,t=[],n=``,r=document.querySelector(`#budgetSearchInput`),i=document.querySelector(`#budgetsList`),a=document.querySelector(`#refreshBudgetsButton`),o=document.querySelector(`#resetAllButton`);async function s(){try{let n=await fetch(`${e}/budgets`);if(!n.ok){i.innerHTML=`<p class="empty">Erro ao carregar orçamentos.</p>`;return}t=await n.json(),c()}catch(e){console.error(`Erro ao carregar orçamentos:`,e),i.innerHTML=`<p class="empty">Erro de conexão com a API.</p>`}}function c(){let e=t.filter(e=>e.customerName.toLowerCase().includes(n.toLowerCase()));if(e.length===0){i.innerHTML=`<p class="empty">Nenhum orçamento encontrado.</p>`;return}i.innerHTML=e.map(e=>`
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
          </div>
        </article>
      `).join(``)}async function l(t,n){try{if(!(await fetch(`${e}/budgets/${t}/status`,{method:`PATCH`,headers:{"Content-Type":`application/json`},body:JSON.stringify({status:n})})).ok){alert(`Erro ao atualizar status.`);return}await s()}catch(e){console.error(`Erro ao atualizar status:`,e),alert(`Erro de conexão com a API.`)}}async function u(){if(confirm(`Deseja limpar todos os dados da POC?`))try{await fetch(`${e}/simulator/reset-all`,{method:`POST`}),await s()}catch(e){console.error(`Erro ao resetar POC:`,e),alert(`Erro de conexão com a API.`)}}a.addEventListener(`click`,s),o.addEventListener(`click`,u),i.addEventListener(`click`,async e=>{let t=e.target,n=t.dataset.budgetId,r=t.dataset.status;!n||!r||await l(n,r)}),r.addEventListener(`input`,e=>{n=e.target.value.trim(),c()}),s();