// Tiếp nhận đơn thuốc (3.2.1): đổi thuốc → nạp đơn vị kê và nhãn đơn vị của liều mỗi lần; hiện dị ứng đã ghi của khách.
// Thêm/bỏ dòng do catalog.js đảm nhận; ô chọn gõ để tìm do combobox.js; thêm nhanh khách hàng do pos.js.
// Kiểm tra hợp lệ, liều dùng, chống chỉ định, dị ứng, tương tác: server làm khi bấm "Kiểm tra đơn" / "Lưu đơn".
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-rx-form]');
  if (!form) return;
  var number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });
  var dataEl = document.getElementById('pm-rx-medicines');
  var medicines = {};
  try { medicines = JSON.parse(dataEl ? dataEl.textContent : '{}') || {}; } catch (e) { medicines = {}; }

  function fillUnits(row) {
    var medSelect = row.querySelector('[data-pm-rx-medicine]');
    var unitSelect = row.querySelector('[data-pm-rx-unit]');
    var baseLabel = row.querySelector('[data-pm-rx-base]');
    if (!medSelect || !unitSelect) return;
    var med = medicines[medSelect.value];
    unitSelect.innerHTML = '';
    if (baseLabel) baseLabel.textContent = med ? med.baseUnit : 'đơn vị';
    if (!med) {
      unitSelect.add(new Option('— Chọn thuốc trước —', ''));
      return;
    }
    med.units.forEach(function (u, k) {
      var label = u.name + (u.factor === 1 ? '' : ' (= ' + number.format(u.factor) + ' ' + med.baseUnit + ')');
      unitSelect.add(new Option(label, String(u.id), k === 0, k === 0));
    });
  }

  function refreshAllergy() {
    var select = form.querySelector('[data-pm-customer]');
    var out = form.querySelector('[data-pm-allergy]');
    if (!select || !out) return;
    var opt = select.options[select.selectedIndex];
    var allergy = opt ? opt.getAttribute('data-allergy') : '';
    out.textContent = allergy ? 'Dị ứng đã ghi: ' + allergy : '';
  }

  // Kết quả kiểm tra cũ không còn đúng khi đơn bị sửa → ẩn, bỏ tích xác nhận, nhắc kiểm tra lại
  var result = form.querySelector('[data-pm-rx-result]');
  var stale = form.querySelector('[data-pm-rx-stale]');
  function markStale(target) {
    if (!result || !result.firstElementChild || result.hidden) return;
    if (target && target.matches('#AcknowledgeWarnings, [name="Notes"]')) return; // ghi chú không ảnh hưởng kết quả
    result.hidden = true;
    var ack = result.querySelector('#AcknowledgeWarnings');
    if (ack) ack.checked = false;
    if (stale) stale.hidden = false;
  }

  form.addEventListener('change', function (e) {
    var row = e.target.closest('[data-pm-rx-line]');
    if (row && e.target.matches('[data-pm-rx-medicine]')) fillUnits(row);
    if (e.target.matches('[data-pm-customer]')) refreshAllergy();
    markStale(e.target);
  });
  form.addEventListener('input', function (e) { markStale(e.target); });
  form.addEventListener('click', function (e) {
    if (e.target.closest('[data-pm-remove], [data-pm-add]')) markStale(null);
  });

  // ---- Gợi ý tên bác sĩ, cơ sở khám khi gõ (từ các đơn đã lưu); gõ không dấu vẫn ra; tên mới vẫn nhập được ----
  var suggest = { doctors: [], clinics: [] };
  var suggestEl = document.getElementById('pm-rx-suggest');
  try { suggest = JSON.parse(suggestEl ? suggestEl.textContent : '{}') || suggest; } catch (e) { /* bỏ qua */ }

  function fold(text) {
    return String(text || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
      .replace(/[đĐ]/g, 'd').toLowerCase().replace(/\s+/g, ' ').trim();
  }

  var suggestSeq = 0;
  function initSuggest(input) {
    var kind = input.getAttribute('data-pm-suggest');
    var items = (kind === 'doctors' ? suggest.doctors || [] : (suggest.clinics || []).map(function (c) { return { name: c }; }))
      .map(function (it) { return { name: it.name, clinic: it.clinic || '', key: fold(it.name) }; });
    if (items.length === 0) return;
    var list = document.createElement('ul');
    list.className = 'pm-combobox-list';
    list.id = 'pm-suggest-' + (++suggestSeq);
    list.setAttribute('role', 'listbox');
    list.hidden = true;
    input.parentNode.appendChild(list);
    input.setAttribute('role', 'combobox');
    input.setAttribute('aria-autocomplete', 'list');
    input.setAttribute('aria-expanded', 'false');
    input.setAttribute('aria-controls', list.id);
    var shown = [];
    var active = -1;

    function close() {
      list.hidden = true;
      input.setAttribute('aria-expanded', 'false');
      input.removeAttribute('aria-activedescendant');
      active = -1;
    }
    function setActive(k) {
      var options = list.querySelectorAll('[role="option"]');
      options.forEach(function (o, i) { o.classList.toggle('active', i === k); });
      active = k;
      if (k >= 0 && options[k]) {
        input.setAttribute('aria-activedescendant', options[k].id);
        options[k].scrollIntoView({ block: 'nearest' });
      }
    }
    function render() {
      var q = fold(input.value);
      list.innerHTML = '';
      if (q === '') { close(); return; }
      var starts = [];
      var words = [];
      var contains = [];
      items.forEach(function (it) {
        if (it.key === q) return; // đã gõ đúng tên này
        var at = it.key.indexOf(q);
        if (at === 0) starts.push(it);
        else if (at > 0 && it.key.charAt(at - 1) === ' ') words.push(it);
        else if (at > 0) contains.push(it);
      });
      shown = starts.concat(words, contains).slice(0, 8);
      if (shown.length === 0) { close(); return; }
      shown.forEach(function (it, i) {
        var li = document.createElement('li');
        li.id = list.id + '-' + i;
        li.setAttribute('role', 'option');
        li.textContent = it.name;
        if (it.clinic) {
          var sub = document.createElement('span');
          sub.className = 'd-block small text-muted-pm';
          sub.textContent = it.clinic;
          li.appendChild(sub);
        }
        list.appendChild(li);
      });
      list.hidden = false;
      input.setAttribute('aria-expanded', 'true');
      setActive(-1);
    }
    function choose(i) {
      var it = shown[i];
      if (!it) return;
      input.value = it.name;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      // Chọn bác sĩ: ô cơ sở khám còn trống thì điền cơ sở khám gần nhất của bác sĩ đó
      if (kind === 'doctors' && it.clinic) {
        var clinic = form.querySelector('[data-pm-suggest="clinics"]');
        if (clinic && clinic.value.trim() === '') {
          clinic.value = it.clinic;
          clinic.dispatchEvent(new Event('input', { bubbles: true }));
        }
      }
      close();
      input.focus();
    }

    input.addEventListener('input', function (e) {
      if (e.isTrusted) render();
    });
    input.addEventListener('keydown', function (e) {
      if (list.hidden) {
        if (e.key === 'ArrowDown' && input.value.trim() !== '') { render(); e.preventDefault(); }
        return;
      }
      if (e.key === 'ArrowDown') { setActive(Math.min(active + 1, shown.length - 1)); e.preventDefault(); }
      else if (e.key === 'ArrowUp') { setActive(Math.max(active - 1, 0)); e.preventDefault(); }
      else if (e.key === 'Enter' && active >= 0) { choose(active); e.preventDefault(); }
      else if (e.key === 'Escape') { close(); e.preventDefault(); }
    });
    list.addEventListener('mousedown', function (e) {
      var li = e.target.closest('[role="option"]');
      if (!li) return;
      e.preventDefault(); // giữ con trỏ trong ô
      choose(Array.prototype.indexOf.call(list.children, li));
    });
    input.addEventListener('blur', function () { window.setTimeout(close, 100); });
  }
  form.querySelectorAll('[data-pm-suggest]').forEach(initSuggest);

  refreshAllergy();
})();
