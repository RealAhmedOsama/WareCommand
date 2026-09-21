// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.addEventListener('DOMContentLoaded', function () {
  const language = (document.documentElement.lang || '').toLowerCase();
  if (language.startsWith('ar') && window.jQuery?.validator) {
    const $ = window.jQuery;
    const messages = {
      required: 'هذا الحقل مطلوب.',
      email: 'أدخل بريداً إلكترونياً صحيحاً.',
      maxlength: 'القيمة أطول من الحد المسموح.',
      minlength: 'القيمة أقصر من الحد المسموح.',
      range: 'القيمة خارج النطاق المسموح.',
      equalTo: 'القيمتان غير متطابقتين.'
    };
    $.extend($.validator.messages, messages);

    document.querySelectorAll('form').forEach(function (form) {
      const validation = $(form).data('unobtrusiveValidation');
      const formMessages = validation?.options?.messages;
      if (!formMessages) {
        return;
      }

      Object.keys(formMessages).forEach(function (fieldName) {
        Object.keys(formMessages[fieldName]).forEach(function (ruleName) {
          if (messages[ruleName]) {
            formMessages[fieldName][ruleName] = messages[ruleName];
          }
        });
      });
    });
  }

  document.querySelectorAll('[data-clear-form]').forEach(function (button) {
    button.addEventListener('click', function () {
      const form = button.closest('form');
      if (form) {
        form.reset();
      }

      const focusField = button.dataset.focusField;
      if (focusField) {
        document.getElementById(focusField)?.focus();
      }
    });
  });

  document.querySelectorAll('[data-reload-page]').forEach(function (button) {
    button.addEventListener('click', function () {
      window.location.reload();
    });
  });

  const quickScanInput = document.querySelector('[data-wms-scan-input]');
  const focusQuickScan = function () {
    if (!(quickScanInput instanceof HTMLInputElement)) {
      return;
    }

    quickScanInput.focus({ preventScroll: true });
    quickScanInput.select();
  };

  document.addEventListener('keydown', function (event) {
    const target = event.target;
    const isTyping = target instanceof HTMLElement && (
      target.isContentEditable ||
      ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
    const isCommandShortcut = (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k';

    if (quickScanInput && ((event.key === '/' && !isTyping) || isCommandShortcut)) {
      event.preventDefault();
      focusQuickScan();
    }
  });

  if (quickScanInput instanceof HTMLInputElement) {
    quickScanInput.addEventListener('keydown', function (event) {
      if (event.key === 'Escape') {
        quickScanInput.value = '';
      }
    });
  }

  if (document.body.dataset.wmsPersistentScan === 'true' && quickScanInput instanceof HTMLInputElement) {
    window.requestAnimationFrame(function () {
      if (document.activeElement === document.body) {
        focusQuickScan();
      }
    });
  }

  const connectivityBanner = document.querySelector('[data-wms-connectivity]');
  const connectivityText = connectivityBanner?.querySelector('[data-wms-connectivity-text]');
  const connectivityIcon = connectivityBanner?.querySelector('[data-wms-connectivity-icon]');
  const connectivityRetry = connectivityBanner?.querySelector('[data-wms-connectivity-retry]');
  const updateButton = connectivityBanner?.querySelector('[data-wms-update]');
  const connectivityMessages = {
    offline: document.body.dataset.wmsOfflineMessage || 'Offline — changes are not being sent.',
    reconnecting: document.body.dataset.wmsReconnectingMessage || 'Reconnecting…',
    update: document.body.dataset.wmsUpdateMessage || 'A new version is ready.'
  };

  const setConnectivity = function (state, message) {
    if (!(connectivityBanner instanceof HTMLElement)) {
      return;
    }

    connectivityBanner.dataset.wmsConnectivityState = state;
    connectivityBanner.hidden = state === 'online' && !message;
    if (connectivityText instanceof HTMLElement) {
      connectivityText.textContent = message || connectivityMessages[state] || '';
    }

    if (connectivityIcon instanceof HTMLElement) {
      connectivityIcon.className = state === 'reconnecting' || state === 'update'
        ? 'bi bi-arrow-repeat'
        : state === 'blocked'
          ? 'bi bi-exclamation-triangle'
          : 'bi bi-wifi-off';
      connectivityIcon.setAttribute('aria-hidden', 'true');
    }

    if (connectivityRetry instanceof HTMLButtonElement) {
      connectivityRetry.hidden = state !== 'offline' && state !== 'blocked';
    }
  };

  const checkConnectivity = function () {
    if (!navigator.onLine) {
      setConnectivity('offline');
      return;
    }

    setConnectivity('reconnecting');
    fetch('/health/live', {
      cache: 'no-store',
      credentials: 'same-origin',
      headers: { 'X-Wms-Connectivity': '1' }
    }).then(function (response) {
      if (!response.ok) {
        throw new Error('Connectivity probe failed.');
      }
      setConnectivity('online');
    }).catch(function () {
      setConnectivity('offline');
    });
  };

  window.addEventListener('offline', function () {
    setConnectivity('offline');
  });
  window.addEventListener('online', checkConnectivity);
  connectivityRetry?.addEventListener('click', checkConnectivity);
  if (!navigator.onLine) {
    setConnectivity('offline');
  }

  document.addEventListener('submit', function (event) {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || navigator.onLine || form.dataset.wmsOfflineCommand === 'true') {
      return;
    }

    const method = (form.getAttribute('method') || 'get').toLowerCase();
    if (method === 'get') {
      return;
    }

    event.preventDefault();
    const message = document.body.dataset.wmsOfflineMutationMessage || 'This action needs a connection. Nothing was submitted.';
    setConnectivity('blocked', message);
    window.setTimeout(function () {
      if (!navigator.onLine) {
        setConnectivity('offline');
      }
    }, 4000);
  }, true);

  if (quickScanInput instanceof HTMLInputElement) {
    const sessionStorageKey = 'wms.quick-scan.v1';
    try {
      const savedValue = window.sessionStorage.getItem(sessionStorageKey);
      if (!quickScanInput.value && savedValue) {
        quickScanInput.value = savedValue;
      }

      quickScanInput.addEventListener('input', function () {
        if (quickScanInput.value) {
          window.sessionStorage.setItem(sessionStorageKey, quickScanInput.value);
        } else {
          window.sessionStorage.removeItem(sessionStorageKey);
        }
      });

      quickScanInput.form?.addEventListener('submit', function () {
        window.sessionStorage.removeItem(sessionStorageKey);
      });
    } catch {
      // Storage may be unavailable in a private or restricted browser context.
    }
  }

  let serviceWorkerRegistration = null;
  let refreshingForServiceWorker = false;
  const announceUpdate = function () {
    setConnectivity('update');
    if (updateButton instanceof HTMLButtonElement) {
      updateButton.hidden = false;
    }
  };

  if ('serviceWorker' in navigator) {
    window.addEventListener('load', function () {
      navigator.serviceWorker.register('/sw.js', { scope: '/', updateViaCache: 'none' })
        .then(function (registration) {
          serviceWorkerRegistration = registration;
          if (registration.waiting && navigator.serviceWorker.controller) {
            announceUpdate();
          }

          registration.addEventListener('updatefound', function () {
            const worker = registration.installing;
            worker?.addEventListener('statechange', function () {
              if (worker.state === 'installed' && navigator.serviceWorker.controller) {
                announceUpdate();
              }
            });
          });
        })
        .catch(function () {
          // PWA support is progressive; the MVC shell remains usable without it.
        });
    });

    navigator.serviceWorker.addEventListener('controllerchange', function () {
      if (refreshingForServiceWorker) {
        return;
      }

      refreshingForServiceWorker = true;
      window.location.reload();
    });
  }

  updateButton?.addEventListener('click', function () {
    if (serviceWorkerRegistration?.waiting) {
      serviceWorkerRegistration.waiting.postMessage({ type: 'SKIP_WAITING' });
    } else {
      window.location.reload();
    }
  });
});
