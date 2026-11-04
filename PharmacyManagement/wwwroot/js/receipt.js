// Form phiếu nhập kho: đổi thuốc → nạp đơn vị nhập của thuốc đó; tạm tính thành tiền; nhắc hạn dùng ngắn.
// Thêm/bỏ dòng do catalog.js đảm nhận (form có data-pm-repeater-form). Server kiểm tra lại toàn bộ khi lưu và khi nhập kho.
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-receipt-form]');
  if (!form) return;

  var money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  var dataEl = document.getElementById('pm-receipt-medicines');
  var medicines = {};
  try { medicines = JSON.parse(dataEl ? dataEl.textContent : '{}') || {}; } catch (e) { medicines = {}; }
  var warnDays = parseInt(form.getAttribute('data-pm-warn-days') || '90', 10);
  var today = parseDate(form.getAttribute('data-pm-today'));

  function parseDate(text) {
    var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text || '');
    return m ? Date.UTC(+m[1], +m[2] - 1, +m[3]) : NaN;
  }
  function moneyOf(text) {
    var digits = String(text || '').replace(/\D/g, '');
    return digits === '' ? NaN : Number(digits);
  }
  function numberOf(text) {
    var n = parseFloat(text);
    return isNaN(n) ? 0 : n;
  }

  // ---- Đổi thuốc: nạp lại danh sách đơn vị, gợi ý đơn giá tham khảo ----
  function fillUnits(row) {
    var medSelect = row.querySelector('[data-pm-medicine]');
    var unitSelect = row.querySelector('[data-pm-unit]');
    if (!medSelect || !unitSelect) return;
    var med = medicines[medSelect.value];
    unitSelect.innerHTML = '';
    if (!med) {
      unitSelect.add(new Option('— Chọn thuốc trước —', ''));
    } else {
      med.units.forEach(function (u, i) {
        var label = u.name + (u.factor === 1 ? '' : ' (= ' + u.factor.toLocaleString('vi-VN') + ' ' + med.units[0].name + ')');
        var opt = new Option(label, String(u.id), false, i === 0);
        opt.setAttribute('data-factor', String(u.factor));
        unitSelect.add(opt);
      });
    }
    refreshHint(row);
  }

  function refreshHint(row) {
    var medSelect = row.querySelector('[data-pm-medicine]');
    var unitSelect = row.querySelector('[data-pm-unit]');
    var cost = row.querySelector('[data-pm-cost]');
    if (!medSelect || !unitSelect || !cost) return;
    var med = medicines[medSelect.value];
    var opt = unitSelect.options[unitSelect.selectedIndex];
    var factor = opt ? parseFloat(opt.getAttribute('data-factor') || '1') : 1;
    cost.placeholder = med && med.price > 0 ? 'Tham khảo ' + money.format(Math.round(med.price * factor)) : 'Đơn giá';
  }

  // ---- Thành tiền = (nhận − hỏng) × đơn giá; tổng phiếu ----
  function refreshTotals() {
    var sum = 0;
    var any = false;
    form.querySelectorAll('[data-pm-receipt-line]').forEach(function (row) {
      var qty = numberOf((row.querySelector('[data-pm-qty]') || {}).value);
      var dmg = numberOf((row.querySelector('[data-pm-damaged]') || {}).value);
      var cost = moneyOf((row.querySelector('[data-pm-cost]') || {}).value);
      var out = row.querySelector('[data-pm-line-total]');
      if (isNaN(cost) || qty <= 0) {
        if (out) out.textContent = '—';
        return;
      }
      var line = Math.max(0, qty - dmg) * cost;
      sum += line;
      any = true;
      if (out) out.textContent = money.format(Math.round(line)) + ' đ';
    });
    var total = form.querySelector('[data-pm-receipt-total]');
    if (total) total.textContent = any ? money.format(Math.round(sum)) + ' đ' : '—';
  }

  // ---- Hạn dùng: hết hạn → không nhập được; còn ≤ ngưỡng → cảnh báo (2.2.1.2) ----
  function refreshExpiry(row) {
    var input = row.querySelector('[data-pm-expiry]');
    var note = row.querySelector('[data-pm-expiry-note]');
    if (!input || !note) return;
    var exp = parseDate(input.value);
    note.textContent = '';
    note.className = 'small d-block mt-1';
    if (isNaN(exp) || isNaN(today)) return;
    var days = Math.round((exp - today) / 86400000);
    if (days <= 0) {
      note.textContent = 'Đã hết hạn — không nhập kho được';
      note.classList.add('text-danger');
    } else if (days <= warnDays) {
      note.textContent = 'Còn ' + days + ' ngày hạn dùng';
      note.classList.add('text-warning-emphasis');
    }
  }

  form.addEventListener('change', function (e) {
    var row = e.target.closest('[data-pm-receipt-line]');
    if (!row) return;
    if (e.target.matches('[data-pm-medicine]')) fillUnits(row);
    else if (e.target.matches('[data-pm-unit]')) refreshHint(row);
    else if (e.target.matches('[data-pm-expiry]')) refreshExpiry(row);
    refreshTotals();
  });
  form.addEventListener('input', function (e) {
    if (e.target.matches('[data-pm-qty], [data-pm-damaged], [data-pm-cost]')) refreshTotals();
    if (e.target.matches('[data-pm-expiry]')) {
      var row = e.target.closest('[data-pm-receipt-line]');
      if (row) refreshExpiry(row);
    }
  });
  // Bỏ dòng (catalog.js xử lý click trước) → tính lại tổng
  form.addEventListener('click', function (e) {
    if (e.target.closest('[data-pm-remove]')) setTimeout(refreshTotals, 0);
  });

  form.querySelectorAll('[data-pm-receipt-line]').forEach(function (row) {
    if (row.closest('template')) return;
    refreshExpiry(row);
  });
  refreshTotals();
})();
