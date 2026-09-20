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
});
