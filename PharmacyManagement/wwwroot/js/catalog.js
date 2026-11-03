// Form thuốc: thêm/bỏ dòng (hoạt chất, quy đổi), nhãn đơn vị cơ sở, giá tự tính. Server vẫn kiểm tra lại toàn bộ.
// Phần thêm/bỏ dòng dùng chung cho form khác có data-pm-repeater-form (dòng dị ứng ở hồ sơ khách hàng).
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-medicine-form], [data-pm-repeater-form]');
  if (!form) return;
  var money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 });

  // ---- Thêm / bỏ dòng ----
  form.querySelectorAll('[data-pm-add]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var name = btn.getAttribute('data-pm-add');
      var box = form.querySelector('[data-pm-repeater="' + name + '"]');
      var tpl = box && box.querySelector('template[data-pm-template="' + name + '"]');
      if (!tpl) return;
      var next = parseInt(box.getAttribute('data-pm-next') || '0', 10);
      box.setAttribute('data-pm-next', String(next + 1));
      var holder = document.createElement('div');
      holder.innerHTML = tpl.innerHTML.replace(/__i__/g, 'n' + next);
      var row = holder.firstElementChild;
      box.insertBefore(row, tpl);
      var first = row.querySelector('select, input:not([type=hidden])');
      if (first) first.focus();
      refreshPrices();
    });
  });
  form.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-pm-remove]');
    if (!btn) return;
    var row = btn.closest('[data-pm-row]');
    var box = row && row.parentElement;
    if (row) row.remove();
    var add = box && form.querySelector('[data-pm-add="' + box.getAttribute('data-pm-repeater') + '"]');
    if (add) add.focus();
  });

  // ---- Chỉ một đơn vị mặc định khi bán ----
  form.addEventListener('change', function (e) {
    var t = e.target;
    if (t.matches && t.matches('[data-pm-single-check]') && t.checked) {
      form.querySelectorAll('[data-pm-single-check="' + t.getAttribute('data-pm-single-check') + '"]').forEach(function (other) {
        if (other !== t) other.checked = false;
      });
    }
  });

  // ---- Tên đơn vị cơ sở trong các nhãn ----
  var baseUnit = form.querySelector('[data-pm-base-unit]');
  function baseUnitName() {
    if (!baseUnit) return 'đơn vị cơ sở';
    if (baseUnit.tagName === 'SELECT') {
      var opt = baseUnit.options[baseUnit.selectedIndex];
      return opt && opt.value ? opt.text : 'đơn vị cơ sở';
    }
    return baseUnit.getAttribute('data-unit-name') || 'đơn vị cơ sở';
  }
  function refreshUnitNames() {
    var name = baseUnitName();
    form.querySelectorAll('[data-pm-base-unit-name]').forEach(function (el) { el.textContent = name; });
  }
  if (baseUnit) baseUnit.addEventListener('change', refreshUnitNames);

  // ---- Gợi ý giá tự tính = giá đơn vị cơ sở × hệ số ----
  function basePrice() {
    var input = form.querySelector('[data-pm-base-price-input]');
    if (input) return parseFloat(input.value);
    var locked = form.querySelector('[data-pm-base-price]');
    return locked ? parseFloat(locked.getAttribute('data-pm-base-price')) : NaN;
  }
  function refreshPrices() {
    var price = basePrice();
    form.querySelectorAll('[data-pm-auto-price]').forEach(function (input) {
      var row = input.closest('[data-pm-row]');
      var factorInput = row && row.querySelector('[data-pm-factor-input]');
      var factorLocked = row && row.querySelector('[data-pm-factor]');
      var factor = factorInput ? parseFloat(factorInput.value) : (factorLocked ? parseFloat(factorLocked.getAttribute('data-pm-factor')) : NaN);
      input.placeholder = (price > 0 && factor > 0) ? 'Tự tính: ' + money.format(Math.round(price * factor * 100) / 100) + ' đ' : 'Tự tính';
    });
  }
  form.addEventListener('input', function (e) {
    if (e.target.matches('[data-pm-base-price-input], [data-pm-factor-input]')) refreshPrices();
  });

  refreshUnitNames();
  refreshPrices();
})();
