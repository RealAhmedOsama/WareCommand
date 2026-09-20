// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.addEventListener('DOMContentLoaded', function () {
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
