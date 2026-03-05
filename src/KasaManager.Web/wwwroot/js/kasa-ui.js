/* ============================================================
   KasaManager UI Helpers
   Alpine.js stores, HTMX config, utilities
   ============================================================ */

// ---------- 1. Dark Mode Store ----------
document.addEventListener('alpine:init', () => {
  Alpine.store('darkMode', {
    on: localStorage.getItem('kasa-theme') !== 'light',  // default: dark

    toggle() {
      this.on = !this.on;
      const theme = this.on ? 'dark' : 'light';
      document.documentElement.setAttribute('data-theme', theme);
      document.documentElement.setAttribute('data-bs-theme', theme);
      localStorage.setItem('kasa-theme', theme);
      document.cookie = 'kasa-theme=' + theme + ';path=/;max-age=31536000;SameSite=Lax';
    },

    init() {
      const theme = this.on ? 'dark' : 'light';
      document.documentElement.setAttribute('data-theme', theme);
      document.documentElement.setAttribute('data-bs-theme', theme);
      document.cookie = 'kasa-theme=' + theme + ';path=/;max-age=31536000;SameSite=Lax';
    }
  });


  Alpine.store('sidebar', {
    collapsed: localStorage.getItem('kasa-sidebar') === 'collapsed',
    mobileOpen: false,

    toggle() {
      this.collapsed = !this.collapsed;
      localStorage.setItem('kasa-sidebar', this.collapsed ? 'collapsed' : 'expanded');
    },

    openMobile() {
      this.mobileOpen = true;
    },

    closeMobile() {
      this.mobileOpen = false;
    }
  });
});

// Theme IIFE moved to <head> in _Layout.cshtml for earliest possible execution

// ---------- 2. Turkish Money Formatter ----------
function formatTurkishMoney(value, decimals = 2) {
  if (value === null || value === undefined || isNaN(value)) return '-';
  const num = parseFloat(value);
  return num.toLocaleString('tr-TR', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals
  });
}

function moneyClass(value) {
  if (value === null || value === undefined) return 'money-zero';
  const num = parseFloat(value);
  if (num > 0) return 'money-positive';
  if (num < 0) return 'money-negative';
  return 'money-zero';
}

// ---------- 3. IBAN Copy ----------
async function copyIban(btn, iban) {
  try {
    await navigator.clipboard.writeText(iban.replace(/\s/g, ''));
    btn.classList.add('copied');
    const origHtml = btn.innerHTML;
    btn.innerHTML = '<i class="fas fa-check"></i> Kopyalandı';
    setTimeout(() => {
      btn.classList.remove('copied');
      btn.innerHTML = origHtml;
    }, 2000);
  } catch {
    // Fallback
    const ta = document.createElement('textarea');
    ta.value = iban.replace(/\s/g, '');
    document.body.appendChild(ta);
    ta.select();
    document.execCommand('copy');
    document.body.removeChild(ta);
    btn.classList.add('copied');
    setTimeout(() => btn.classList.remove('copied'), 2000);
  }
}

// KasaIBAN: Kart bazlı IBAN kopyalama (Bankaya Götürülecek kartlar)
const KasaIBAN = {
  async copy(iban) {
    const clean = iban.replace(/\s/g, '').replace(/-/g, '');
    try {
      await navigator.clipboard.writeText(clean);
      KasaToast.success('IBAN kopyalandı: ' + clean.substring(0, 8) + '...', 2500);
    } catch {
      // Fallback
      const ta = document.createElement('textarea');
      ta.value = clean;
      ta.style.position = 'fixed';
      ta.style.left = '-9999px';
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
      KasaToast.success('IBAN kopyalandı', 2500);
    }
  }
};

// ---------- 4. Toast Manager ----------
const KasaToast = {
  container: null,

  init() {
    this.container = document.getElementById('kasa-toast-container');
  },

  show(type, message, duration = 4000) {
    if (!this.container) this.init();
    if (!this.container) return;

    const icons = {
      success: 'fas fa-check',
      error: 'fas fa-times',
      warning: 'fas fa-exclamation',
      info: 'fas fa-info'
    };

    const toast = document.createElement('div');
    toast.className = `kasa-toast kasa-toast-${type}`;
    toast.innerHTML = `
      <div class="kasa-toast-icon"><i class="${icons[type] || icons.info}"></i></div>
      <div style="flex:1">${message}</div>
      <button class="kasa-toast-close" onclick="this.parentElement.classList.add('removing'); setTimeout(() => this.parentElement.remove(), 300)">
        <i class="fas fa-times"></i>
      </button>
    `;

    this.container.appendChild(toast);

    if (duration > 0) {
      setTimeout(() => {
        if (toast.parentElement) {
          toast.classList.add('removing');
          setTimeout(() => toast.remove(), 300);
        }
      }, duration);
    }
  },

  success(msg, dur) { this.show('success', msg, dur); },
  error(msg, dur) { this.show('error', msg, dur); },
  warning(msg, dur) { this.show('warning', msg, dur); },
  info(msg, dur) { this.show('info', msg, dur); }
};

// ---------- 5. Hidden Value Toggle ----------
function toggleHiddenValue(btn) {
  const wrapper = btn.closest('.hidden-value');
  const mask = wrapper.querySelector('.hidden-value-mask');
  const actual = wrapper.querySelector('.hidden-value-actual');
  const icon = btn.querySelector('i');

  if (actual.style.display === 'none') {
    actual.style.display = 'inline';
    mask.style.display = 'none';
    icon.className = 'fas fa-eye-slash';
  } else {
    actual.style.display = 'none';
    mask.style.display = 'inline';
    icon.className = 'fas fa-eye';
  }
}

// ---------- 6. Clock ----------
function updateClock() {
  const el = document.getElementById('kasa-clock');
  if (!el) return;
  const now = new Date();
  const opts = { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false };
  el.textContent = now.toLocaleTimeString('tr-TR', opts);
}

function updateDate() {
  const el = document.getElementById('kasa-date');
  if (!el) return;
  const now = new Date();
  const opts = { day: '2-digit', month: 'long', year: 'numeric', weekday: 'long' };
  el.textContent = now.toLocaleDateString('tr-TR', opts);
}

// Init clock
document.addEventListener('DOMContentLoaded', () => {
  updateClock();
  updateDate();
  setInterval(updateClock, 1000);

  // Auto-show toast from TempData (if kasa-alert elements exist)
  document.querySelectorAll('.kasa-alert-auto').forEach(el => {
    const type = el.dataset.type || 'info';
    const msg = el.textContent.trim();
    if (msg) KasaToast.show(type, msg);
    el.remove();
  });
});

// ---------- 7. Loading State ----------
function setLoadingState(btn, loading) {
  if (loading) {
    btn.dataset.origHtml = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<span class="kasa-spinner"></span> Yükleniyor...';
    // Güvenlik: 2 dakika sonra hâlâ aynı sayfadaysak resetle
    btn._loadingTimer = setTimeout(function () {
      if (btn.disabled) {
        btn.disabled = false;
        btn.innerHTML = btn.dataset.origHtml || btn.innerHTML;
        KasaToast.warning('İşlem uzun sürdü veya bir hata oluştu. Tekrar deneyebilirsiniz.');
      }
    }, 120000);
  } else {
    if (btn._loadingTimer) clearTimeout(btn._loadingTimer);
    btn.disabled = false;
    btn.innerHTML = btn.dataset.origHtml || btn.innerHTML;
  }
}

// Sayfa bfcache'den geri yüklendiğinde tüm loading butonları sıfırla
window.addEventListener('pageshow', function (event) {
  if (event.persisted) {
    document.querySelectorAll('button[disabled]').forEach(function (btn) {
      if (btn.dataset.origHtml) {
        btn.disabled = false;
        btn.innerHTML = btn.dataset.origHtml;
      }
    });
  }
});

// ---------- 8. Dropzone Handler ----------
function formatFileSize(bytes) {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return (bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1) + ' ' + units[i];
}

function initDropzone(dropzoneId, fileInputId, formId) {
  const dropzone = document.getElementById(dropzoneId);
  const fileInput = document.getElementById(fileInputId);
  const form = document.getElementById(formId);
  if (!dropzone || !fileInput) return;

  const allowedExtensions = ['.xlsx', '.xls'];
  let dragCounter = 0;

  // Click to select
  dropzone.addEventListener('click', (e) => {
    if (e.target.closest('.kasa-btn')) return; // don't intercept button clicks
    fileInput.click();
  });

  // Drag events
  dropzone.addEventListener('dragenter', (e) => {
    e.preventDefault();
    dragCounter++;
    dropzone.classList.add('drag-over');
  });

  dropzone.addEventListener('dragleave', (e) => {
    e.preventDefault();
    dragCounter--;
    if (dragCounter <= 0) {
      dragCounter = 0;
      dropzone.classList.remove('drag-over');
    }
  });

  dropzone.addEventListener('dragover', (e) => {
    e.preventDefault();
  });

  dropzone.addEventListener('drop', (e) => {
    e.preventDefault();
    dragCounter = 0;
    dropzone.classList.remove('drag-over');

    const files = e.dataTransfer.files;
    if (files.length > 0) {
      // Filter valid files
      const validFiles = new DataTransfer();
      let rejected = 0;
      for (const f of files) {
        const ext = '.' + f.name.split('.').pop().toLowerCase();
        if (allowedExtensions.includes(ext)) {
          validFiles.items.add(f);
        } else {
          rejected++;
        }
      }

      if (rejected > 0) {
        KasaToast.warning(`${rejected} dosya reddedildi (sadece .xlsx, .xls kabul edilir)`);
      }

      if (validFiles.files.length > 0) {
        fileInput.files = validFiles.files;
        showSelectedFiles(dropzone, validFiles.files);
      }
    }
  });

  // File input change
  fileInput.addEventListener('change', () => {
    if (fileInput.files.length > 0) {
      showSelectedFiles(dropzone, fileInput.files);
    }
  });
}

function showSelectedFiles(dropzone, files) {
  let container = dropzone.querySelector('.kasa-dropzone-files');
  if (!container) {
    container = document.createElement('div');
    container.className = 'kasa-dropzone-files';
    dropzone.appendChild(container);
  }
  container.innerHTML = '';

  for (const f of files) {
    const chip = document.createElement('div');
    chip.className = 'kasa-dropzone-file';
    chip.innerHTML = `<i class="fas fa-file-excel"></i> ${f.name} <span style="color:var(--text-muted)">(${formatFileSize(f.size)})</span>`;
    container.appendChild(chip);
  }

  // Show the upload button
  const btn = dropzone.closest('form')?.querySelector('.kasa-btn-upload');
  if (btn) {
    btn.style.display = 'inline-flex';
  }
}

// ---------- 9. Detail Section Toggle ----------
function toggleDetailSection(btn, sectionId) {
  const section = document.getElementById(sectionId);
  if (!section) return;
  const isHidden = section.style.display === 'none';
  section.style.display = isHidden ? '' : 'none';
  btn.setAttribute('aria-expanded', isHidden ? 'true' : 'false');
}
