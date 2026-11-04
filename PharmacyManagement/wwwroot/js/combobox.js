// Ô chọn có tìm kiếm: biến <select data-pm-combobox> thành ô gõ chữ, gõ đến đâu hiện gợi ý đến đó.
// - Tìm không phân biệt dấu, hoa/thường; khớp chữ của lựa chọn + từ khóa trong data-search (ví dụ tên hoạt chất).
// - Tên BẮT ĐẦU bằng chữ gõ lên trước; tối đa 50 gợi ý.
// - Chuột, hoặc ↑ ↓ + Enter để chọn; Esc đóng. Bấm ra ngoài: giữ lựa chọn cũ; xóa trống ô rồi bấm ra ngoài: bỏ chọn.
// - <select> gốc vẫn nằm trong form (ẩn) và giữ giá trị được gửi lên server; chọn xong phát sự kiện "change" như chọn thường.
// - Không có JavaScript: vẫn là danh sách thả xuống bình thường. Ô thêm động (dòng mới) được xử lý tự động.
// Thuộc tính tùy chọn: data-pm-combobox-placeholder, data-pm-combobox-empty.
(function () {
  'use strict';
  var MAX_RESULTS = 50;
  var seq = 0;

  function fold(text) {
    return String(text || '')
      .normalize('NFD').replace(/[̀-ͯ]/g, '')
      .replace(/đ/g, 'd').replace(/Đ/g, 'D')
      .toLowerCase().replace(/\s+/g, ' ').trim();
  }

  function enhance(select) {
    if (select.hasAttribute('data-pm-combobox-ready') || select.closest('template')) return;
    select.setAttribute('data-pm-combobox-ready', '');
    var id = 'pm-cb-' + (++seq);
    var hadFocus = document.activeElement === select;

    var wrap = document.createElement('div');
    wrap.className = 'pm-combobox';
    var input = document.createElement('input');
    input.type = 'text';
    input.className = 'form-control';
    input.autocomplete = 'off';
    input.spellcheck = false;
    input.placeholder = select.getAttribute('data-pm-combobox-placeholder') || 'Gõ để tìm…';
    input.setAttribute('role', 'combobox');
    input.setAttribute('aria-autocomplete', 'list');
    input.setAttribute('aria-expanded', 'false');
    input.setAttribute('aria-controls', id + '-list');
    // <label for="..."> trỏ vào ô gõ
    if (select.id) {
      input.id = select.id;
      select.id = select.id + '-select';
    }
    var list = document.createElement('ul');
    list.id = id + '-list';
    list.className = 'pm-combobox-list';
    list.setAttribute('role', 'listbox');
    list.hidden = true;

    select.parentNode.insertBefore(wrap, select);
    wrap.appendChild(input);
    wrap.appendChild(list);
    wrap.appendChild(select);
    select.hidden = true;
    select.tabIndex = -1;
    select.setAttribute('aria-hidden', 'true');

    var items = Array.prototype.filter.call(select.options, function (o) { return o.value !== ''; })
      .map(function (o) {
        return { value: o.value, label: o.text, name: fold(o.text), key: fold(o.text + ' ' + (o.getAttribute('data-search') || '')) };
      });
    var shown = [];
    var active = -1;

    function currentLabel() {
      var o = select.options[select.selectedIndex];
      return o && o.value !== '' ? o.text : '';
    }

    function open() {
      list.hidden = false;
      input.setAttribute('aria-expanded', 'true');
    }

    function close() {
      list.hidden = true;
      input.setAttribute('aria-expanded', 'false');
      input.removeAttribute('aria-activedescendant');
      active = -1;
    }

    function setActive(i) {
      var options = list.querySelectorAll('[role="option"]');
      for (var k = 0; k < options.length; k++) options[k].classList.remove('active');
      active = i;
      if (i >= 0 && options[i]) {
        options[i].classList.add('active');
        input.setAttribute('aria-activedescendant', options[i].id);
        options[i].scrollIntoView({ block: 'nearest' });
      } else {
        input.removeAttribute('aria-activedescendant');
      }
    }

    function render(query) {
      var q = fold(query);
      var starts = [];
      var contains = [];
      items.forEach(function (it) {
        if (q === '') contains.push(it);
        else if (it.name.indexOf(q) === 0) starts.push(it);
        else if (it.key.indexOf(q) !== -1) contains.push(it);
      });
      shown = starts.concat(contains).slice(0, MAX_RESULTS);
      list.innerHTML = '';
      if (shown.length === 0) {
        var empty = document.createElement('li');
        empty.className = 'pm-combobox-empty';
        empty.textContent = select.getAttribute('data-pm-combobox-empty') || 'Không có kết quả';
        list.appendChild(empty);
      }
      shown.forEach(function (it, i) {
        var li = document.createElement('li');
        li.id = id + '-opt-' + i;
        li.setAttribute('role', 'option');
        li.setAttribute('data-value', it.value);
        li.setAttribute('aria-selected', it.value === select.value ? 'true' : 'false');
        li.textContent = it.label;
        list.appendChild(li);
      });
      open();
      setActive(shown.length > 0 ? 0 : -1);
    }

    function choose(value) {
      if (select.value !== value) {
        select.value = value;
        select.dispatchEvent(new Event('change', { bubbles: true }));
      }
      input.value = currentLabel();
      close();
    }

    input.value = currentLabel();

    input.addEventListener('input', function () { render(input.value); });
    input.addEventListener('focus', function () { input.select(); });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (list.hidden) render(input.value === currentLabel() ? '' : input.value);
        else setActive(Math.min(active + 1, shown.length - 1));
      } else if (e.key === 'ArrowUp') {
        if (!list.hidden) {
          e.preventDefault();
          setActive(Math.max(active - 1, 0));
        }
      } else if (e.key === 'Enter') {
        if (!list.hidden) {
          e.preventDefault(); // không gửi form khi đang chọn
          if (active >= 0 && shown[active]) choose(shown[active].value);
        }
      } else if (e.key === 'Escape') {
        if (!list.hidden) {
          e.preventDefault();
          input.value = currentLabel();
          close();
        }
      }
    });
    // mousedown (trước blur) để chọn được bằng chuột
    list.addEventListener('mousedown', function (e) {
      var li = e.target.closest('[role="option"]');
      e.preventDefault();
      if (li) choose(li.getAttribute('data-value'));
    });
    input.addEventListener('blur', function () {
      if (input.value.trim() === '') choose('');
      else input.value = currentLabel();
      close();
    });
    // Giá trị đổi từ nơi khác (ví dụ trình duyệt khôi phục form)
    select.addEventListener('change', function () {
      if (document.activeElement !== input) input.value = currentLabel();
    });

    if (hadFocus) input.focus();
  }

  function scan(root) {
    if (root.matches && root.matches('select[data-pm-combobox]')) enhance(root);
    if (root.querySelectorAll) root.querySelectorAll('select[data-pm-combobox]').forEach(enhance);
  }

  scan(document);
  if (window.MutationObserver) {
    new MutationObserver(function (mutations) {
      mutations.forEach(function (m) {
        m.addedNodes.forEach(function (n) { if (n.nodeType === 1) scan(n); });
      });
    }).observe(document.body, { childList: true, subtree: true });
  }
})();
