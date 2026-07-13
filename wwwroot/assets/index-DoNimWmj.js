(function(){let e=document.createElement(`link`).relList;if(e&&e.supports&&e.supports(`modulepreload`))return;for(let e of document.querySelectorAll(`link[rel="modulepreload"]`))n(e);new MutationObserver(e=>{for(let t of e)if(t.type===`childList`)for(let e of t.addedNodes)e.tagName===`LINK`&&e.rel===`modulepreload`&&n(e)}).observe(document,{childList:!0,subtree:!0});function t(e){let t={};return e.integrity&&(t.integrity=e.integrity),e.referrerPolicy&&(t.referrerPolicy=e.referrerPolicy),e.crossOrigin===`use-credentials`?t.credentials=`include`:e.crossOrigin===`anonymous`?t.credentials=`omit`:t.credentials=`same-origin`,t}function n(e){if(e.ep)return;e.ep=!0;let n=t(e);fetch(e.href,n)}})();var e=window.location.origin,t=13,n=14,r=24,i=[],a=[],o=``,s=document.querySelector(`#budgetSearchInput`),c=document.querySelector(`#budgetsList`),l=document.querySelector(`#downloadBudgetsButton`),u=document.querySelector(`#refreshBudgetsButton`),ee=document.querySelector(`#resetAllButton`),d=document.querySelector(`#waitingList`),f=document.querySelector(`#refreshWaitingButton`),p=document.querySelector(`#canceledList`),m=document.querySelector(`#refreshCanceledButton`);async function h(){try{let t=await fetch(`${e}/budgets`);if(!t.ok){c.innerHTML=`<p class="empty">Erro ao carregar orçamentos.</p>`;return}i=await t.json(),b(),v()}catch(e){console.error(`Erro ao carregar orçamentos:`,e),c.innerHTML=`<p class="empty">Erro de conexão com a API.</p>`}}async function g(){try{let t=await fetch(`${e}/conversations`);if(!t.ok){d.innerHTML=`<p class="empty">Erro ao carregar clientes em espera.</p>`;return}a=await t.json(),_()}catch(e){console.error(`Erro ao carregar clientes em espera:`,e),d.innerHTML=`<p class="empty">Erro de conexão com a API.</p>`}}function _(){let e=a.filter(e=>e.stage===t||e.stage===r);if(e.length===0){d.innerHTML=`<p class="empty">Nenhum cliente em espera.</p>`;return}d.innerHTML=e.map(e=>{let t=e.stage===r,n=t?`<span class="status status-attended">Já foi atendido</span>`:`<span class="status status-waiting">Em espera</span>`,i=t?`<button class="revert-button" data-attend-phone="${e.phone}" data-attended="false">Cliente não atendido</button>`:`<button class="attend-button" data-attend-phone="${e.phone}" data-attended="true">Cliente atendido</button>`;return`
        <article class="budget-card${t?` attended`:``}">
          <div class="budget-header">
            <strong>${e.customerName??`Cliente`}</strong>
            ${n}
          </div>

          <p><b>Telefone:</b> ${e.phone}</p>
          <p><b>Atualizado em:</b> ${y(e.updatedAt)}</p>

          <div class="status-actions">
            ${i}
          </div>
        </article>
      `}).join(``)}function v(){let e=i.filter(e=>e.status.toLowerCase().includes(`cancelado`));if(e.length===0){p.innerHTML=`<p class="empty">Nenhum orçamento cancelado.</p>`;return}p.innerHTML=e.map(e=>`
        <article class="budget-card">
          <div class="budget-header">
            <strong>${e.customerName}</strong>
            <span class="status status-canceled">${e.status}</span>
          </div>

          <p><b>Telefone:</b> ${e.phone}</p>
          <p><b>Produto:</b> ${e.product}</p>
          <p><b>Quantidade:</b> ${e.quantity}</p>
          <p><b>Tipo:</b> ${e.deliveryType}</p>
          <p><b>Atualizado em:</b> ${y(e.updatedAt)}</p>
        </article>
      `).join(``)}function y(e){return new Date(e).toLocaleString(`pt-BR`)}function b(){let e=i.filter(e=>e.customerName.toLowerCase().includes(o.toLowerCase()));if(e.length===0){c.innerHTML=`<p class="empty">Nenhum orçamento encontrado.</p>`;return}c.innerHTML=e.map(e=>`
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

            <button data-budget-id="${e.id}" data-status="Fechado">Fechado</button>
            <button data-budget-id="${e.id}" data-status="Perdido">Perdido</button>
            <button data-budget-id="${e.id}" data-status="Atendido">Cliente atendido</button>
          </div>
        </article>
      `).join(``)}function x(){if(i.length===0){alert(`Nenhum orçamento para exportar.`);return}let e=[[`Cliente`,`Telefone`,`Possui cadastro`,`Lista de materiais`,`Tipo de entrega`,`Status`,`Atualizado em`],...i.map(e=>[e.customerName,e.phone,e.hasRegistration?`Sim`:`Não`,e.product,e.deliveryType,e.status,new Date(e.updatedAt).toLocaleString(`pt-BR`)])].map(e=>e.map(e=>`"${String(e??``).replace(/"/g,`""`)}"`).join(`,`)).join(`
`),t=new Blob([`﻿`+e],{type:`text/csv;charset=utf-8;`}),n=URL.createObjectURL(t),r=document.createElement(`a`),a=new Date().toISOString().slice(0,10);r.href=n,r.download=`orcamentos_${a}.csv`,document.body.appendChild(r),r.click(),document.body.removeChild(r),URL.revokeObjectURL(n)}async function S(t,n){try{if(!(await fetch(`${e}/budgets/${t}/status`,{method:`PATCH`,headers:{"Content-Type":`application/json`},body:JSON.stringify({status:n})})).ok){alert(`Erro ao atualizar status.`);return}await h()}catch(e){console.error(`Erro ao atualizar status:`,e),alert(`Erro de conexão com a API.`)}}async function C(t,n){try{if(!(await fetch(`${e}/conversations/${t}/attendance`,{method:`PATCH`,headers:{"Content-Type":`application/json`},body:JSON.stringify({attended:n})})).ok){alert(`Erro ao atualizar atendimento.`);return}await g()}catch(e){console.error(`Erro ao atualizar atendimento:`,e),alert(`Erro de conexão com a API.`)}}var w=`Joel123400`,T=document.querySelector(`#passwordModal`),E=document.querySelector(`#passwordInput`),D=document.querySelector(`#passwordError`),O=document.querySelector(`#modalCancelButton`),k=document.querySelector(`#modalConfirmButton`);function A(){E.value=``,D.classList.add(`hidden`),T.classList.remove(`hidden`),E.focus()}function j(){T.classList.add(`hidden`)}async function M(){if(E.value!==w){D.classList.remove(`hidden`),E.value=``,E.focus();return}j();try{await fetch(`${e}/simulator/reset-all`,{method:`POST`}),await h(),await g()}catch(e){console.error(`Erro ao resetar:`,e),alert(`Erro de conexão com a API.`)}}u.addEventListener(`click`,h),f.addEventListener(`click`,g),m.addEventListener(`click`,h),ee.addEventListener(`click`,A),O.addEventListener(`click`,j),k.addEventListener(`click`,M),E.addEventListener(`keydown`,e=>{e.key===`Enter`&&M(),e.key===`Escape`&&j()}),T.addEventListener(`click`,e=>{e.target===T&&j()}),c.addEventListener(`click`,async e=>{let t=e.target,n=t.dataset.budgetId,r=t.dataset.status;!n||!r||await S(n,r)}),d.addEventListener(`click`,async e=>{let t=e.target,n=t.dataset.attendPhone;n&&await C(n,t.dataset.attended===`true`)}),s.addEventListener(`input`,e=>{o=e.target.value.trim(),b()});var N=document.querySelector(`#chatPasswordModal`),P=document.querySelector(`#chatPasswordInput`),F=document.querySelector(`#chatModalCancelButton`),I=document.querySelector(`#chatModalConfirmButton`),L=document.querySelector(`#chatModal`),R=document.querySelector(`#chatCloseButton`),z=document.querySelector(`#chatButton`),B=document.querySelector(`#chatConversationList`),V=document.querySelector(`#chatHeader`),H=document.querySelector(`#chatMessages`),U=document.querySelector(`#chatInput`),W=document.querySelector(`#chatSendButton`),G=document.querySelector(`#chatRefreshButton`),K=null,q=null;function J(){N.classList.add(`hidden`)}function Y(){L.classList.remove(`hidden`),te()}z.addEventListener(`click`,Y),F.addEventListener(`click`,J),I.addEventListener(`click`,Y),R.addEventListener(`click`,()=>{L.classList.add(`hidden`),ne()}),P.addEventListener(`keydown`,e=>{e.key===`Enter`&&Y(),e.key===`Escape`&&J()}),N.addEventListener(`click`,e=>{e.target===N&&J()}),h(),g();async function X(){try{let i=(await(await fetch(`${e}/conversations`)).json()).filter(e=>e.stage===t||e.stage===r||e.stage===n);B.innerHTML=``,i.forEach(e=>{let r=document.createElement(`div`);r.className=`chat-conv-item`,e.phone===q&&r.classList.add(`active`);let i=e.stage===t?`<span class="status status-waiting">Aguardando</span>`:e.stage===n?`<span class="status status-attended">Orçamento</span>`:`<span class="status status-attended">Atendido</span>`;r.innerHTML=`
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:0.2rem;">
          <span class="chat-conv-name">${e.customerName||`Desconhecido`}</span>
          ${i}
        </div>
        <div class="chat-conv-phone">${e.phone}</div>
      `,r.addEventListener(`click`,()=>Z(e.phone,e.customerName||void 0)),B.appendChild(r)})}catch(e){console.error(`Erro ao carregar conversas do chat:`,e)}}async function Z(e,t){q=e,V.textContent=t||e,document.querySelectorAll(`.chat-conv-item`).forEach(t=>{t.classList.remove(`active`),t.innerHTML.includes(e)&&t.classList.add(`active`)}),await Q()}async function Q(){if(q)try{let t=await(await fetch(`${e}/conversations/${q}/messages`)).json();H.innerHTML=``,t.forEach(e=>{let t=document.createElement(`div`);t.className=`chat-bubble ${e.from}`,e.from===`audio`?t.innerHTML=`
          <div style="display:flex;align-items:center;gap:0.5rem;">
            <span>🎤</span>
            <audio controls style="height:32px;max-width:200px;">
              <source src="/audio/${e.text}" type="audio/ogg">
            </audio>
          </div>
        `:t.textContent=e.text,H.appendChild(t)}),H.scrollTop=H.scrollHeight}catch(e){console.error(`Erro ao carregar mensagens:`,e)}}async function $(){let t=U.value.trim();if(!(!q||!t))try{await fetch(`${e}/conversations/${q}/send`,{method:`POST`,headers:{"Content-Type":`application/json`},body:JSON.stringify({message:t})}),U.value=``,await Q()}catch(e){console.error(`Erro ao enviar mensagem:`,e)}}function te(){X(),K&&clearInterval(K),K=window.setInterval(()=>{X()},3e3)}function ne(){K&&=(clearInterval(K),null),q=null,V.textContent=`Selecione uma conversa`,H.innerHTML=``,B.innerHTML=``}W.addEventListener(`click`,$),U.addEventListener(`keydown`,e=>{e.key===`Enter`&&$()}),G.addEventListener(`click`,Q),l.addEventListener(`click`,x);