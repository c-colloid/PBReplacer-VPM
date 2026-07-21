// PBReplacer VPM Listing page
// ビルド時テンプレート(Scriban)に依存せず、実行時に index.json を読んで描画する自己完結実装。
// index.json は package-list-action が生成する VPM リポジトリリスティング。
(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);

  // "1.2.3-beta.4" 形式の簡易semver比較(降順ソート用)。プレリリースは同一本体バージョンの安定版より下位。
  function parseVersion(v) {
    const [core, pre] = String(v).split('-', 2);
    const nums = core.split('.').map(n => parseInt(n, 10) || 0);
    while (nums.length < 3) nums.push(0);
    return { nums, pre: pre ?? null };
  }

  function compareVersionDesc(a, b) {
    const pa = parseVersion(a), pb = parseVersion(b);
    for (let i = 0; i < 3; i++) {
      if (pa.nums[i] !== pb.nums[i]) return pb.nums[i] - pa.nums[i];
    }
    if (pa.pre === null && pb.pre === null) return 0;
    if (pa.pre === null) return -1; // 安定版が先
    if (pb.pre === null) return 1;
    const ia = pa.pre.split('.'), ib = pb.pre.split('.');
    for (let i = 0; i < Math.max(ia.length, ib.length); i++) {
      if (ia[i] === undefined) return 1;
      if (ib[i] === undefined) return -1;
      const na = parseInt(ia[i], 10), nb = parseInt(ib[i], 10);
      const bothNum = !isNaN(na) && !isNaN(nb);
      const c = bothNum ? nb - na : ib[i].localeCompare(ia[i]);
      if (c !== 0) return c;
    }
    return 0;
  }

  const isPrerelease = (v) => String(v).includes('-');

  function showToast(message) {
    const toast = $('copyToast');
    toast.textContent = message;
    toast.hidden = false;
    clearTimeout(showToast._t);
    showToast._t = setTimeout(() => { toast.hidden = true; }, 1600);
  }

  function setupHeader(listing) {
    document.title = `${listing.name ?? 'VPM Listing'} - VPM Listing`;
    $('listingName').textContent = listing.name ?? '';

    const banner = $('bannerImage');
    banner.style.backgroundImage = 'url(banner.png)';
    banner.hidden = false;

    if (listing.description) $('listingDescription').textContent = listing.description;

    const author = listing.author;
    if (author && (author.name || author.url)) {
      const wrap = $('publishedBy');
      wrap.textContent = 'Published by ';
      if (author.url) {
        const a = document.createElement('a');
        a.href = author.url;
        a.target = '_blank';
        a.rel = 'noopener';
        a.textContent = author.name ?? author.url;
        wrap.appendChild(a);
      } else {
        wrap.appendChild(document.createTextNode(author.name));
      }
    }

    if (listing.infoLink && listing.infoLink.url) {
      $('infoLinkWrap').hidden = false;
      const link = $('infoLink');
      link.href = listing.infoLink.url;
      link.textContent = listing.infoLink.text || 'Learn More';
      const footerLink = $('footerRepoLink');
      footerLink.href = listing.infoLink.url;
      footerLink.hidden = false;
    }
  }

  function setupVccButtons(listingUrl) {
    const urlField = $('vccUrlField');
    urlField.value = listingUrl;

    const addButton = $('vccAddRepoButton');
    addButton.href = `vcc://vpm/addRepo?url=${encodeURIComponent(listingUrl)}`;
    addButton.removeAttribute('aria-disabled');

    const copyButton = $('vccUrlFieldCopy');
    copyButton.disabled = false;
    copyButton.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(listingUrl);
        showToast('Copied!');
      } catch (e) {
        urlField.select();
        document.execCommand('copy');
        showToast('Copied!');
      }
    });
  }

  function renderPackage(packageId, versionsMap) {
    const versions = Object.keys(versionsMap).sort(compareVersionDesc);
    if (versions.length === 0) return null;

    const latestStable = versions.find(v => !isPrerelease(v));
    const latest = latestStable ?? versions[0];
    const latestManifest = versionsMap[latest];
    const latestPre = versions.find(v => isPrerelease(v));

    const card = document.createElement('article');
    card.className = 'package-card';

    const title = document.createElement('div');
    title.className = 'package-title';
    const nameEl = document.createElement('h2');
    nameEl.textContent = latestManifest.displayName || packageId;
    const idEl = document.createElement('code');
    idEl.className = 'package-id';
    idEl.textContent = packageId;
    title.appendChild(nameEl);
    title.appendChild(idEl);
    card.appendChild(title);

    if (latestManifest.description) {
      const desc = document.createElement('p');
      desc.className = 'package-desc';
      desc.textContent = latestManifest.description;
      card.appendChild(desc);
    }

    const meta = document.createElement('p');
    meta.className = 'caption';
    let metaText = `Latest: ${latest}`;
    if (latestPre && latestPre !== latest) metaText += ` / Pre-release: ${latestPre}`;
    const deps = latestManifest.vpmDependencies;
    if (deps && Object.keys(deps).length > 0) {
      metaText += ` · Requires: ${Object.keys(deps).join(', ')}`;
    }
    meta.textContent = metaText;
    card.appendChild(meta);

    const details = document.createElement('details');
    const summary = document.createElement('summary');
    summary.textContent = `All versions (${versions.length})`;
    details.appendChild(summary);
    const list = document.createElement('ul');
    list.className = 'version-list';
    for (const v of versions) {
      const li = document.createElement('li');
      const label = document.createElement('span');
      label.textContent = v;
      li.appendChild(label);
      if (isPrerelease(v)) {
        const badge = document.createElement('span');
        badge.className = 'badge';
        badge.textContent = 'pre-release';
        li.appendChild(badge);
      }
      const zipUrl = versionsMap[v] && versionsMap[v].url;
      if (zipUrl) {
        const dl = document.createElement('a');
        dl.href = zipUrl;
        dl.textContent = 'zip';
        dl.className = 'dl-link';
        li.appendChild(dl);
      }
      list.appendChild(li);
    }
    details.appendChild(list);
    card.appendChild(details);

    return card;
  }

  async function main() {
    let listing;
    try {
      const res = await fetch('index.json', { cache: 'no-cache' });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      listing = await res.json();
    } catch (e) {
      console.error('Failed to load index.json:', e);
      $('loadError').hidden = false;
      return;
    }

    setupHeader(listing);
    if (listing.url) setupVccButtons(listing.url);

    const packagesRoot = $('packages');
    const packages = listing.packages ?? {};
    for (const [packageId, entry] of Object.entries(packages)) {
      const card = renderPackage(packageId, entry.versions ?? {});
      if (card) packagesRoot.appendChild(card);
    }
  }

  main();
})();
