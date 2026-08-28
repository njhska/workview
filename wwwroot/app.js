const list = document.querySelector('#file-list');
const crumbs = document.querySelector('#breadcrumbs');
const dialog = document.querySelector('#preview-dialog');
const enc = encodeURIComponent;
let currentPath = '';

const formatSize = n => n == null ? '—' : n < 1024 ? `${n} B` : n < 1048576 ? `${(n/1024).toFixed(1)} KB` : `${(n/1048576).toFixed(1)} MB`;
const formatDate = value => new Intl.DateTimeFormat('zh-CN',{year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit'}).format(new Date(value));
const join = (base,name) => base ? `${base}/${name}` : name;
const extension = name => (name.split('.').pop() || 'TXT').slice(0,4).toUpperCase();

async function load(path='') {
  list.innerHTML = '<div class="message">正在读取目录…</div>';
  try {
    const response = await fetch(`/api/files?path=${enc(path)}`);
    const data = await response.json();
    if (!response.ok) throw new Error(data.error);
    currentPath = data.path;
    renderCrumbs();
    list.replaceChildren();
    if (!data.items.length) list.innerHTML = '<div class="message">这个目录是空的。</div>';
    for (const item of data.items) {
      const row = document.createElement('button');
      row.className = `file-row ${item.isDirectory ? 'directory' : ''}`;
      row.disabled = !item.isDirectory && !item.previewable;
      row.innerHTML = `<span class="name"><span class="icon">${item.isDirectory?'DIR':extension(item.name)}</span><span class="filename"></span></span><span class="size">${item.isDirectory?'—':formatSize(item.size)}</span><span class="date">${formatDate(item.lastModifiedUtc)}</span>`;
      row.querySelector('.filename').textContent = item.name;
      row.title = !item.isDirectory && !item.previewable ? '此文件不支持预览' : item.name;
      row.addEventListener('click', () => item.isDirectory ? load(join(currentPath,item.name)) : preview(join(currentPath,item.name)));
      list.append(row);
    }
  } catch (error) { list.innerHTML = `<div class="message">${escapeHtml(error.message || '读取失败')}</div>`; }
}

function renderCrumbs() {
  crumbs.replaceChildren();
  const parts = currentPath ? currentPath.split('/') : [];
  [['根目录',''], ...parts.map((p,i)=>[p,parts.slice(0,i+1).join('/')])].forEach(([name,path],i) => {
    if(i) crumbs.insertAdjacentHTML('beforeend','<span class="crumb-sep">/</span>');
    const button=document.createElement('button'); button.textContent=name; button.onclick=()=>load(path); crumbs.append(button);
  });
}

async function preview(path) {
  try {
    const response=await fetch(`/api/preview?path=${enc(path)}`); const data=await response.json();
    if(!response.ok) throw new Error(data.error);
    document.querySelector('#preview-name').textContent=data.name;
    document.querySelector('#preview-path').textContent=data.path;
    document.querySelector('#preview-type').textContent=extension(data.name);
    document.querySelector('#preview-content').textContent=data.content;
    document.querySelector('#line-numbers').textContent=Array.from({length:data.content.split('\n').length},(_,i)=>i+1).join('\n');
    dialog.showModal();
  } catch(error) { alert(error.message || '预览失败'); }
}
function escapeHtml(value){const d=document.createElement('div');d.textContent=value;return d.innerHTML}
document.querySelector('#close-preview').onclick=()=>dialog.close();
dialog.addEventListener('click',e=>{if(e.target===dialog)dialog.close()});
load();
