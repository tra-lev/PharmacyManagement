// Đơn đặt hàng (2.1.1): đổi thuốc → nạp đơn vị đặt (mặc định đơn vị lớn nhất) + đơn giá nhập gần nhất theo đơn vị;
// tạm tính thành tiền, tổng đơn; hiện tồn khả dụng và mức tối thiểu của thuốc. Server kiểm tra lại toàn bộ khi lưu.
// Thêm/bỏ dòng do catalog.js; ô chọn gõ để tìm do combobox.js; ô tiền định dạng do money-input.js.
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-order-form]');
  if (!form) return;
  var money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  var number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });
  var dataEl = document.getElementById('pm-order-medicines');
  var medicines = {};
  try { medicines = JSON.parse(dataEl ? dataEl.textContent : '{}') || {}; } catch (e) { medicines = {}; }

  function moneyOf(text) {
    var digits = String(text || '').replace(/\D/g, '');
    return digits === '' ? NaN : Number(digits);
  }

  function currentUnit(row) {
    var med = medicines[(row.querySelector('[data-pm-medicine]') || {}).value];
    var unitSelect = row.querySelector('[data-pm-unit]');
    if (!med || !unitSelect) return null;
    var found = null;
    med.units.forEach(function (u) { if (String(u.id) === unitSelect.value) found = u; });
    return found;
  }

  // Đơn giá: điền theo giá gần nhất khi ô trống hoặc đang là giá tự điền (người dùng chưa sửa)
  function fillPrice(row) {
    var price = row.querySelector('[data-pm-price]');
    var unit = currentUnit(row);
    if (!price || !unit) return;
    if (price.value.trim() === '' || price.getAttribute('data-pm-auto') === 'true') {
      price.value = unit.price > 0 ? money.format(unit.price) : '';
      price.setAttribute('data-pm-auto', 'true');
    }
  }

  function fillUnits(row) {
    var med = medicines[(row.querySelector('[data-pm-medicine]') || {}).value];
    var unitSelect = row.querySelector('[data-pm-unit]');
    if (!unitSelect) return;
    unitSelect.innerHTML = '';
    if (!med) {
      unitSelect.add(new Option('— Chọn thuốc trước —', ''));
      return;
    }
    med.units.forEach(function (u, k) {
      var last = k === med.units.length - 1;
      var label = u.name + (u.factor === 1 ? '' : ' (= ' + number.format(u.factor) + ' ' + med.baseUnit + ')');
      unitSelect.add(new Option(label, String(u.id), last, last));
    });
    var price = row.querySelector('[data-pm-price]');
    if (price) price.setAttribute('data-pm-auto', 'true');
    fillPrice(row);
  }

  function refresh() {
    var sum = 0;
    var any = false;
    form.querySelectorAll('[data-pm-order-line]').forEach(function (row) {
      if (row.closest('template')) return;
      var qty = parseFloat((row.querySelector('[data-pm-qty]') || {}).value);
      var price = moneyOf((row.querySelector('[data-pm-price]') || {}).value);
      var out = row.querySelector('[data-pm-line-total]');
      var note = row.querySelector('[data-pm-stock-note]');
      var med = medicines[(row.querySelector('[data-pm-medicine]') || {}).value];
      if (note) {
        note.textContent = med
          ? 'Tồn hiện có ' + number.format(med.stock) + ' ' + med.baseUnit + (med.min !== null && med.min !== undefined ? ' · tối thiểu ' + number.format(med.min) + ' ' + med.baseUnit : '')
          : '';
        note.className = 'small d-block ' + (med && med.min !== null && med.min !== undefined && med.stock < med.min ? 'text-danger' : 'text-muted-pm');
      }
      if (!(qty > 0) || isNaN(price)) {
        if (out) out.textContent = '—';
        return;
      }
      var line = qty * price;
      sum += line;
      any = true;
      if (out) out.textContent = money.format(Math.round(line)) + ' đ';
    });
    var total = form.querySelector('[data-pm-order-total]');
    if (total) total.textContent = any ? money.format(Math.round(sum)) + ' đ' : '—';
  }

  form.addEventListener('change', function (e) {
    var row = e.target.closest('[data-pm-order-line]');
    if (row && e.target.matches('[data-pm-medicine]')) fillUnits(row);
    else if (row && e.target.matches('[data-pm-unit]')) fillPrice(row);
    refresh();
  });
  form.addEventListener('input', function (e) {
    if (e.target.matches('[data-pm-price]')) e.target.setAttribute('data-pm-auto', 'false');
    if (e.target.matches('[data-pm-qty], [data-pm-price]')) refresh();
  });
  form.addEventListener('click', function (e) {
    if (e.target.closest('[data-pm-remove]')) setTimeout(refresh, 0);
  });
  refresh();
})();
