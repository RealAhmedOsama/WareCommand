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
});
