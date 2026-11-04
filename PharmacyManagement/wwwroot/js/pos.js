// Bán lẻ (3.1.4, 3.3.1) và thu tiền (3.3.2) — chỉ tính TẠM để người bán thấy ngay; server tính lại và kiểm tra toàn bộ khi lưu.
// Form bán (data-pm-sale-form): đổi thuốc → nạp đơn vị + giá; thành tiền, khuyến mãi tốt nhất, cảnh báo vượt tồn,
//   giảm thêm theo % (giới hạn chính sách giá); thêm nhanh khách hàng tại quầy (cửa sổ #quick-customer, gửi bằng fetch).
//   Thêm/bỏ dòng do catalog.js đảm nhận (data-pm-repeater-form); ô chọn thuốc/khách gõ để tìm do combobox.js đảm nhận.
// Form thu tiền (data-pm-pay-form): chọn phương thức → hiện ô tương ứng; tiền thối, gợi ý tiền khách đưa;
//   chuyển khoản: mã VietQR có sẵn số tiền + nội dung = mã hóa đơn (vẽ bằng wwwroot/lib/qrcode-generator, không cần Internet).
(function () {
  'use strict';
  var money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  var number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });

  function moneyOf(text) {
    var digits = String(text || '').replace(/\D/g, '');
    return digits === '' ? 0 : Number(digits);
  }
  function vnd(value) {
    return money.format(Math.round(value)) + ' đ';
  }

  // ================================================================ Form bán lẻ
  var form = document.querySelector('[data-pm-sale-form]');
  if (form) initSale(form);

  function initSale(form) {
    var dataEl = document.getElementById('pm-sale-medicines');
    var medicines = {};
    try { medicines = JSON.parse(dataEl ? dataEl.textContent : '{}') || {}; } catch (e) { medicines = {}; }
    var maxPct = parseFloat(form.getAttribute('data-pm-max-discount') || '0') || 0;
    var overLimit = form.getAttribute('data-pm-over-limit') === 'true'; // quyền SYS09.DiscountOverLimit

    function unitLabel(u, baseUnit) {
      return u.name + (u.factor === 1 ? '' : ' (= ' + number.format(u.factor) + ' ' + baseUnit + ')') + ' · ' + vnd(u.price);
    }

    function fillUnits(row) {
      var medSelect = row.querySelector('[data-pm-medicine]');
      var unitSelect = row.querySelector('[data-pm-unit]');
      if (!medSelect || !unitSelect) return;
      var med = medicines[medSelect.value];
      unitSelect.innerHTML = '';
      if (!med) {
        unitSelect.add(new Option('— Chọn thuốc trước —', ''));
        return;
      }
      med.units.forEach(function (u) {
        unitSelect.add(new Option(unitLabel(u, med.baseUnit), String(u.id), u.def, u.def));
      });
    }

    // Cùng quy tắc SalesInvoiceService.BestPromotion: % làm tròn xuống 100 đ; số tiền × số đơn vị cơ sở; lấy mức giảm nhiều nhất
    function bestPromotion(gross, baseQty, promos) {
      var best = { amount: 0, name: null };
      (promos || []).forEach(function (p) {
        var d;
        if (p.pct !== null && p.pct !== undefined) d = Math.floor(gross * p.pct / 100 / 100) * 100;
        else if (p.amt !== null && p.amt !== undefined) d = Math.round(p.amt * baseQty);
        else return;
        d = Math.min(Math.max(d, 0), gross);
        if (d > best.amount) best = { amount: d, name: p.name };
      });
      return best;
    }

    // Cùng công thức SalesInvoiceService.ExtraDiscountAmount: % × tiền hàng, làm tròn xuống 100 đ (tính bằng số nguyên cho khớp server)
    function extraAmount(subTotal, pct) {
      var pct10 = Math.round(pct * 10);
      return pct10 <= 0 ? 0 : Math.floor(subTotal * pct10 / 100000) * 100;
    }

    function refresh() {
      var subTotal = 0;
      var promoTotal = 0;
      var any = false;
      var needByMed = {};
      var rows = [];
      form.querySelectorAll('[data-pm-sale-line]').forEach(function (row) {
        if (row.closest('template')) return;
        var medSelect = row.querySelector('[data-pm-medicine]');
        var unitSelect = row.querySelector('[data-pm-unit]');
        var qtyInput = row.querySelector('[data-pm-qty]');
        var med = medSelect ? medicines[medSelect.value] : null;
        var unit = null;
        if (med && unitSelect) {
          med.units.forEach(function (u) { if (String(u.id) === unitSelect.value) unit = u; });
        }
        var qty = qtyInput ? parseInt(qtyInput.value, 10) : NaN;
        var info = { row: row, med: med, medId: medSelect ? medSelect.value : '', unit: unit, qty: qty };
        if (med && unit && qty > 0) {
          info.gross = qty * unit.price;
          info.baseQty = Math.round(qty * unit.factor * 1000) / 1000;
          info.promo = bestPromotion(info.gross, info.baseQty, med.promos);
          subTotal += info.gross;
          promoTotal += info.promo.amount;
          needByMed[info.medId] = (needByMed[info.medId] || 0) + info.baseQty;
          any = true;
        }
        rows.push(info);
      });

      rows.forEach(function (info) {
        var out = info.row.querySelector('[data-pm-line-total]');
        var note = info.row.querySelector('[data-pm-line-note]');
        if (note) { note.textContent = ''; note.className = 'small d-block'; }
        if (info.gross === undefined) {
          if (out) out.textContent = '—';
          return;
        }
        if (out) out.textContent = vnd(info.gross - info.promo.amount);
        var parts = [];
        if (info.promo.amount > 0) parts.push('Khuyến mãi “' + info.promo.name + '”: −' + vnd(info.promo.amount));
        if (info.unit.factor !== 1) parts.push('= ' + number.format(info.baseQty) + ' ' + info.med.baseUnit);
        var need = needByMed[info.medId];
        if (need > info.med.avail) {
          parts.push(info.med.avail > 0
            ? 'Chỉ còn ' + number.format(info.med.avail) + ' ' + info.med.baseUnit
            : 'Hết hàng');
          if (note) note.classList.add('text-danger');
        } else if (note) {
          note.classList.add('text-muted-pm');
        }
        if (note) note.textContent = parts.join(' · ');
      });

      // ---- Giảm thêm theo % ----
      var pctInput = form.querySelector('[data-pm-extra-pct]');
      var pct = pctInput ? parseFloat(pctInput.value) : 0;
      if (isNaN(pct) || pct < 0) pct = 0;
      var extra = extraAmount(subTotal, pct);
      var allowed = overLimit ? subTotal - promoTotal : Math.max(0, Math.floor(subTotal * maxPct / 100) - promoTotal);
      var allowedPct = subTotal > 0 ? Math.floor(allowed * 1000 / subTotal) / 10 : 0;
      var amountOut = form.querySelector('[data-pm-extra-amount]');
      if (amountOut) amountOut.textContent = any && extra > 0 ? '= −' + vnd(extra) : '';
      var hint = form.querySelector('[data-pm-extra-hint]');
      if (hint && any && (overLimit || maxPct > 0)) {
        var tooMuch = extra > allowed;
        if (overLimit) {
          hint.textContent = tooMuch
            ? 'Giảm tối đa ' + number.format(allowedPct) + '%.'
            : 'Bạn được giảm vượt ' + number.format(maxPct) + '%.';
        } else {
          hint.textContent = tooMuch
            ? 'Vượt quá mức giảm tối đa ' + number.format(maxPct) + '%.'
            : 'Giảm tối đa ' + number.format(maxPct) + '%.';
        }
        hint.className = 'form-text' + (tooMuch ? ' text-danger' : '');
      }

      setText('[data-pm-subtotal]', any ? vnd(subTotal) : '—');
      setText('[data-pm-promo]', any ? (promoTotal > 0 ? '−' + vnd(promoTotal) : '0 đ') : '—');
      setText('[data-pm-total]', any ? vnd(Math.max(0, subTotal - promoTotal - extra)) : '—');
    }

    function setText(selector, text) {
      var el = form.querySelector(selector);
      if (el) el.textContent = text;
    }

    function refreshAllergy() {
      var select = form.querySelector('[data-pm-customer]');
      var out = form.querySelector('[data-pm-allergy]');
      if (!select || !out) return;
      var opt = select.options[select.selectedIndex];
      var allergy = opt ? opt.getAttribute('data-allergy') : '';
      out.textContent = allergy ? 'Dị ứng đã ghi: ' + allergy : '';
    }

    form.addEventListener('change', function (e) {
      var row = e.target.closest('[data-pm-sale-line]');
      if (row && e.target.matches('[data-pm-medicine]')) fillUnits(row);
      if (e.target.matches('[data-pm-customer]')) refreshAllergy();
      refresh();
    });
    form.addEventListener('input', function (e) {
      if (e.target.matches('[data-pm-qty], [data-pm-extra-pct]')) refresh();
    });
    // Bỏ dòng (catalog.js xử lý click trước) → tính lại
    form.addEventListener('click', function (e) {
      if (e.target.closest('[data-pm-remove]')) setTimeout(refresh, 0);
    });

    initQuickCustomer(form);
    refreshAllergy();
    refresh();
  }

  // ---------------- Thêm nhanh khách hàng tại quầy (1.2.2) ----------------
  function initQuickCustomer(saleForm) {
    var select = saleForm.querySelector('[data-pm-customer]');
    var modalEl = document.getElementById('quick-customer');
    if (!select || !modalEl || !window.bootstrap) return;
    var modal = window.bootstrap.Modal.getOrCreateInstance(modalEl);
    var qform = modalEl.querySelector('[data-pm-quick-customer]');
    var errorBox = qform.querySelector('[data-pm-qc-error]');
    var submit = qform.querySelector('[data-pm-qc-submit]');
    var nameInput = qform.querySelector('[name="FullName"]');
    var phoneInput = qform.querySelector('[name="Phone"]');
    var addedNote = saleForm.querySelector('[data-pm-customer-added]');
    var detailsUrl = qform.getAttribute('data-pm-customer-url') || '';

    function clearInvalid() {
      qform.querySelectorAll('[aria-invalid]').forEach(function (el) { el.removeAttribute('aria-invalid'); });
    }
    function showError(message, field) {
      errorBox.textContent = message;
      errorBox.hidden = false;
      clearInvalid();
      var input = field ? qform.querySelector('[name="' + field + '"]') : null;
      if (input) {
        input.setAttribute('aria-invalid', 'true');
        input.focus();
      }
    }

    // combobox.js phát sự kiện này khi chọn dòng "+ Thêm khách hàng mới “…”"
    select.addEventListener('pm-combobox-create', function (e) {
      var query = (e.detail && e.detail.query) || '';
      qform.reset();
      errorBox.hidden = true;
      clearInvalid();
      if (/^[0-9][0-9 .\-]*$/.test(query)) phoneInput.value = query;
      else nameInput.value = query;
      modal.show();
    });
    modalEl.addEventListener('shown.bs.modal', function () {
      (nameInput.value === '' ? nameInput : phoneInput).focus();
    });

    qform.addEventListener('submit', function (e) {
      e.preventDefault();
      if (nameInput.value.trim() === '') {
        showError('Nhập họ tên khách hàng.', 'FullName');
        return;
      }
      submit.disabled = true;
      errorBox.hidden = true;
      fetch(qform.action, {
        method: 'POST',
        body: new FormData(qform),
        credentials: 'same-origin',
        headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' }
      })
        .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error(String(r.status))); })
        .then(function (data) {
          if (!data || !data.ok) {
            showError((data && data.message) || 'Chưa thêm được khách hàng.', data && data.field);
            return;
          }
          var opt = new Option(data.label, String(data.id));
          opt.setAttribute('data-search', data.keywords || '');
          if (data.allergies) opt.setAttribute('data-allergy', data.allergies);
          select.add(opt);
          select.value = String(data.id);
          select.dispatchEvent(new Event('change', { bubbles: true }));
          modal.hide();
          if (addedNote) {
            addedNote.textContent = (data.message || 'Đã thêm khách hàng.') + ' ';
            if (detailsUrl) {
              var link = document.createElement('a');
              link.href = detailsUrl.replace('__id__', String(data.id));
              link.target = '_blank';
              link.rel = 'noopener';
              link.textContent = 'Ghi dị ứng, bệnh nền';
              addedNote.appendChild(link);
            }
          }
        })
        .catch(function () {
          showError('Chưa lưu được. Tải lại trang rồi thử lại.');
        })
        .then(function () { submit.disabled = false; });
    });
  }

  // ================================================================ Thu tiền
  var pay = document.querySelector('[data-pm-pay-form]');
  if (pay) initPay(pay);

  function initPay(pay) {
    var total = parseFloat(pay.getAttribute('data-pm-total') || '0') || 0;
    var code = pay.getAttribute('data-pm-code') || '';
    var inputs = Array.prototype.slice.call(pay.querySelectorAll('[data-pm-pay]'));
    var panels = Array.prototype.slice.call(pay.querySelectorAll('[data-pm-pay-panel]'));
    var methodsBox = pay.querySelector('[data-pm-pay-methods]');
    var radios = Array.prototype.slice.call(pay.querySelectorAll('input[name="pm-pay-method"]'));
    var tendered = pay.querySelector('[data-pm-tendered]');
    var changeOut = pay.querySelector('[data-pm-change]');
    var sumOut = pay.querySelector('[data-pm-pay-sum]');
    var notesBox = pay.querySelector('[data-pm-cash-notes]');
    var qrBox = pay.querySelector('[data-pm-qr]');
    var qrAmount = pay.querySelector('[data-pm-qr-amount]');
    var mixedBox = pay.querySelector('[data-pm-mixed-rest]');
    var restRadios = Array.prototype.slice.call(pay.querySelectorAll('input[name="pm-pay-rest"]'));
    var tenderOnly = Array.prototype.slice.call(pay.querySelectorAll('[data-pm-tender-only]'));
    var mode = null;

    function inputOf(method) {
      return pay.querySelector('[data-pm-pay="' + method + '"]');
    }
    function amountOf(method) {
      var input = inputOf(method);
      return input ? moneyOf(input.value) : 0;
    }

    function setAmount(method, value) {
      var input = inputOf(method);
      if (input) input.value = value > 0 ? money.format(value) : '';
    }

    // ---- Kết hợp: tiền mặt + phần còn lại (chuyển khoản hoặc thẻ, chọn ở data-pm-mixed-rest) ----
    function restMethod() {
      var checked = restRadios.filter(function (r) { return r.checked; })[0];
      return checked ? checked.value : 'transfer';
    }
    function otherMethod() {
      return restMethod() === 'transfer' ? 'card' : 'transfer';
    }
    // Ô vừa sửa giữ nguyên; ô "nhận phần còn lại" = số phải trả − các ô còn lại (không âm)
    function balance(edited) {
      var rest = restMethod();
      var other = otherMethod();
      var cash = amountOf('cash');
      if (edited === 'cash') setAmount(rest, Math.max(0, total - cash - amountOf(other)));
      else if (edited === rest) setAmount(other, Math.max(0, total - cash - amountOf(rest)));
      else if (edited === other) setAmount(rest, Math.max(0, total - cash - amountOf(other)));
    }

    function setMode(next, keepValues) {
      mode = next;
      radios.forEach(function (r) { r.checked = r.value === next; });
      panels.forEach(function (p) { p.hidden = next !== 'mixed' && p.getAttribute('data-pm-pay-panel') !== next; });
      if (mixedBox) mixedBox.hidden = next !== 'mixed';
      // Kết hợp: tiền mặt luôn nhỏ hơn số phải trả nên không có tiền thối → ẩn ô khách đưa, tiền thối, gợi ý mệnh giá
      tenderOnly.forEach(function (el) { el.hidden = next === 'mixed'; });
      inputs.forEach(function (i) {
        var method = i.getAttribute('data-pm-pay');
        if (next === 'mixed') {
          i.readOnly = false;
          if (!keepValues) i.value = '';
        } else {
          // Một hình thức: số tiền = số phải trả, không sửa; hình thức khác để trống (không gửi số tiền)
          i.value = method === next ? money.format(total) : '';
          i.readOnly = method === next;
        }
      });
      if (next !== 'cash' && tendered) tendered.value = '';
      refresh();
      if (next === 'mixed' && !keepValues) {
        var cashInput = inputOf('cash');
        if (cashInput) cashInput.focus();
      }
    }

    function refresh() {
      var sum = 0;
      inputs.forEach(function (i) { sum += moneyOf(i.value); });
      if (sumOut) {
        var diff = total - sum;
        sumOut.textContent = 'Đã nhập ' + vnd(sum) + (diff === 0 ? ' — đủ tiền' : diff > 0 ? ' — còn thiếu ' + vnd(diff) : ' — thừa ' + vnd(-diff));
        if (mode === 'mixed' && amountOf('cash') >= total)
          sumOut.textContent = 'Tiền mặt đã đủ — chọn “Tiền mặt” để tính tiền thối.';
        sumOut.className = 'small ' + (diff === 0 && !(mode === 'mixed' && amountOf('cash') >= total) ? 'text-success' : 'text-danger');
      }
      if (changeOut) {
        var cash = amountOf('cash');
        var given = tendered ? moneyOf(tendered.value) : 0;
        if (!tendered || tendered.value.trim() === '' || cash === 0) changeOut.textContent = '—';
        else if (given < cash) changeOut.textContent = 'Khách đưa chưa đủ';
        else changeOut.textContent = vnd(given - cash);
      }
      refreshNotes();
      refreshQr();
    }

    // Gợi ý tiền khách đưa: vừa đủ + làm tròn lên theo các mệnh giá thường gặp
    var lastNotes = null;
    function refreshNotes() {
      if (!notesBox) return;
      var cash = amountOf('cash');
      if (cash === lastNotes) return;
      lastNotes = cash;
      notesBox.innerHTML = '';
      if (cash <= 0) return;
      var values = [cash];
      [10000, 20000, 50000, 100000, 200000, 500000].forEach(function (d) {
        var v = Math.ceil(cash / d) * d;
        if (values.indexOf(v) === -1) values.push(v);
      });
      values.sort(function (a, b) { return a - b; }).slice(0, 5).forEach(function (v, i) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'btn btn-sm btn-outline-secondary num';
        b.setAttribute('data-pm-tender', String(v));
        b.textContent = i === 0 ? 'Vừa đủ ' + money.format(v) : money.format(v);
        notesBox.appendChild(b);
      });
    }

    // ---- VietQR (chuẩn EMVCo/NAPAS): mã động có số tiền + nội dung chuyển khoản ----
    function tlv(id, value) {
      return id + (value.length < 10 ? '0' : '') + value.length + value;
    }
    function crc16(text) { // CRC-16/CCITT-FALSE
      var crc = 0xFFFF;
      for (var i = 0; i < text.length; i++) {
        crc ^= text.charCodeAt(i) << 8;
        for (var b = 0; b < 8; b++) crc = (crc & 0x8000) ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
      }
      return ('0000' + crc.toString(16).toUpperCase()).slice(-4);
    }
    function vietQrPayload(bin, account, amount, info) {
      var payload = tlv('00', '01') + tlv('01', amount > 0 ? '12' : '11')
        + tlv('38', tlv('00', 'A000000727') + tlv('01', tlv('00', bin) + tlv('01', account)) + tlv('02', 'QRIBFTTA'))
        + tlv('53', '704')
        + (amount > 0 ? tlv('54', String(Math.round(amount))) : '')
        + tlv('58', 'VN')
        + (info ? tlv('62', tlv('08', info)) : '')
        + '6304';
      return payload + crc16(payload);
    }
    function qrSvg(text) {
      var qr = window.qrcode(0, 'M');
      qr.addData(text);
      qr.make();
      var n = qr.getModuleCount();
      var q = 4; // vùng trống bắt buộc quanh mã
      var d = '';
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) if (qr.isDark(r, c)) d += 'M' + (c + q) + ',' + (r + q) + 'h1v1h-1z';
      }
      var size = n + q * 2;
      return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + size + ' ' + size + '" shape-rendering="crispEdges" aria-hidden="true">'
        + '<rect width="' + size + '" height="' + size + '" fill="#fff"/><path d="' + d + '" fill="#000"/></svg>';
    }
    var lastQr = null;
    function refreshQr() {
      if (!qrBox) return;
      var amount = amountOf('transfer');
      if (qrAmount) qrAmount.textContent = amount > 0 ? vnd(amount) : '—';
      var key = amount + '|' + code;
      if (key === lastQr) return;
      lastQr = key;
      if (amount <= 0) {
        qrBox.innerHTML = '<p class="small text-muted-pm m-0 p-3">Nhập số tiền chuyển khoản để tạo mã QR.</p>';
        return;
      }
      if (typeof window.qrcode !== 'function') {
        qrBox.innerHTML = '<p class="small text-danger m-0 p-3">Chưa hiện được mã QR. Tải lại trang.</p>';
        return;
      }
      var info = code.replace(/[^A-Za-z0-9]/g, '').slice(0, 25);
      qrBox.innerHTML = qrSvg(vietQrPayload(qrBox.getAttribute('data-bin'), qrBox.getAttribute('data-account'), amount, info));
    }

    pay.addEventListener('input', function (e) {
      if (mode === 'mixed' && e.target.matches('[data-pm-pay]')) balance(e.target.getAttribute('data-pm-pay'));
      if (e.target.matches('[data-pm-pay], [data-pm-tendered]')) refresh();
    });
    pay.addEventListener('change', function (e) {
      if (e.target.matches('input[name="pm-pay-method"]')) setMode(e.target.value);
      if (e.target.matches('input[name="pm-pay-rest"]')) {
        // Đổi hình thức nhận phần còn lại: chuyển toàn bộ phần chưa trả bằng tiền mặt sang hình thức mới
        setAmount(otherMethod(), 0);
        setAmount(restMethod(), Math.max(0, total - amountOf('cash')));
        refresh();
      }
    });
    pay.addEventListener('click', function (e) {
      var note = e.target.closest('[data-pm-tender]');
      if (note && tendered) {
        tendered.value = money.format(Number(note.getAttribute('data-pm-tender')));
        refresh();
      }
    });

    // Phương thức ban đầu: theo số tiền đã có (khi hiện lại form sau lỗi), mặc định hình thức đầu tiên được phép
    if (methodsBox && radios.length > 0) {
      methodsBox.hidden = false;
      var allowed = radios.map(function (r) { return r.value; });
      var filled = inputs.filter(function (i) { return moneyOf(i.value) > 0; }).map(function (i) { return i.getAttribute('data-pm-pay'); });
      var start = allowed[0];
      if (filled.length === 1 && allowed.indexOf(filled[0]) !== -1 && amountOf(filled[0]) === total) start = filled[0];
      else if (filled.length >= 1 && allowed.indexOf('mixed') !== -1) start = 'mixed';
      // Hiện lại form sau lỗi: giữ số đã nhập; phần còn lại theo hình thức đang có tiền (ưu tiên thẻ nếu chỉ thẻ có tiền)
      if (start === 'mixed' && amountOf('card') > 0 && amountOf('transfer') === 0)
        restRadios.forEach(function (r) { r.checked = r.value === 'card'; });
      setMode(start, start === 'mixed');
    } else {
      refresh();
    }
  }

})();
