(function () {
    'use strict';
    function integer(value) { return /^\d+$/.test(String(value)) && Number.isSafeInteger(Number(value)) ? Number(value) : NaN; }
    function validate(rows, total, days) {
        if (!rows.length || rows.length > 20) return 'Cần từ 1 đến 20 mốc.';
        var last = 0, sum = 0;
        for (var row of rows) {
            if (!row.Title || row.Title.trim().length < 3 || row.Title.trim().length > 180) return 'Tên mỗi mốc cần 3–180 ký tự.';
            if (!Number.isSafeInteger(row.Amount) || row.Amount < 1 || row.Amount > 999999999999) return 'Số tiền mỗi mốc phải là số nguyên VND lớn hơn 0.';
            if (!Number.isSafeInteger(row.DueDay) || row.DueDay < 1 || row.DueDay > days || row.DueDay < last) return 'Ngày hạn phải theo thứ tự và nằm trong thời gian thực hiện.';
            if (!row.AcceptanceCriteria || row.AcceptanceCriteria.trim().length < 10 || row.AcceptanceCriteria.trim().length > 1000) return 'Nêu tiêu chí nghiệm thu 10–1000 ký tự cho từng mốc.';
            last = row.DueDay; sum += row.Amount;
        }
        if (!Number.isSafeInteger(total) || total < 1 || sum !== total) return 'Tổng tiền các mốc phải bằng đúng giá chào.';
        if (JSON.stringify(rows).length > 4000) return 'Kế hoạch quá 4000 ký tự. Hãy rút gọn nội dung.';
        return '';
    }
    if (typeof module !== 'undefined' && module.exports) { module.exports = {integer:integer, validate:validate}; return; }
    document.querySelectorAll('[data-plan-editor]').forEach(function (editor) {
        var form = editor.closest('form'), raw = editor.querySelector('[data-plan-value]'), mode = editor.querySelector('[data-plan-mode]');
        var list = editor.querySelector('[data-plan-rows]'), add = editor.querySelector('[data-plan-add]'), serial = 0;
        function days() { var input = form.querySelector(editor.dataset.planDaysSelector); return integer(input ? input.value : 14); }
        function total() {
            if (editor.dataset.planFixedTotal) return integer(editor.dataset.planFixedTotal);
            var prices = Array.from(form.querySelectorAll(editor.dataset.planTotalSelector));
            return prices.reduce(function (sum, input) { return sum + integer(input.value); },0);
        }
        function values() { return Array.from(list.children).map(function (row) {return {
            Title:row.querySelector('[data-field="Title"]').value.trim(), Amount:integer(row.querySelector('[data-field="Amount"]').value),
            DueDay:integer(row.querySelector('[data-field="DueDay"]').value), AcceptanceCriteria:row.querySelector('[data-field="AcceptanceCriteria"]').value.trim()
        };}); }
        function render() {
            var staged = mode.value === 'staged', rows = values(), amount = total();
            list.hidden = !staged; add.hidden = !staged; add.disabled = list.children.length >= 20;
            editor.querySelector('[data-plan-single]').hidden = staged;
            list.querySelectorAll('input,textarea,button').forEach(function (input) { input.disabled = !staged; });
            var error = staged ? validate(rows, amount, days()) : '';
            raw.value = staged ? JSON.stringify(rows) : ''; mode.setCustomValidity(error);
            var sum = staged ? rows.reduce(function (a,r) {return a+(Number.isFinite(r.Amount)?r.Amount:0);},0) : amount;
            editor.querySelector('[data-plan-feedback]').textContent = 'Tổng mốc: '+(Number.isFinite(sum)?sum.toLocaleString('vi-VN'):'—')+' VND · Giá chào: '+(Number.isFinite(amount)?amount.toLocaleString('vi-VN'):'chưa nhập')+' VND'+(error?' · '+error:'');
            editor.querySelector('[data-plan-feedback]').classList.toggle('error',!!error);
        }
        function append(data) {
            if (list.children.length >= 20) return;
            var row = document.createElement('div'); row.className = 'plan-row'; serial++;
            [['Title','Kết quả cần bàn giao','text'],['Amount','Thanh toán mốc (VND)','number'],['DueDay','Hoàn thành vào ngày thứ','number'],['AcceptanceCriteria','Điều kiện để được nghiệm thu','textarea']].forEach(function (field) {
                var box = document.createElement('div'), label = document.createElement('label'), input = document.createElement(field[2]==='textarea'?'textarea':'input');
                input.id = editor.dataset.planId+'-'+serial+'-'+field[0]; input.dataset.field=field[0]; label.htmlFor=input.id; label.textContent=field[1];
                if(field[2]!=='textarea')input.type=field[2]; input.required=true;
                if(field[2]==='number'){input.min='1';input.step='1';input.max=field[0]==='DueDay'?'3650':'999999999999';}
                else {input.maxLength=field[0]==='Title'?180:1000;input.minLength=field[0]==='Title'?3:10;}
                if(field[2]==='textarea'){input.rows=2;input.placeholder='Ví dụ: Chức năng chạy đúng bộ kiểm thử, có tài liệu và mã nguồn.';}
                input.value=data[field[0]] || ''; box.append(label,input);row.appendChild(box);
            });
            var remove=document.createElement('button');remove.type='button';remove.className='btn btn-outline';remove.textContent='Xóa mốc';
            remove.addEventListener('click',function(){row.remove();render();mode.focus();});row.appendChild(remove);list.appendChild(row);
        }
        var seed=[];
        if(raw.value.trim()) {
            try { seed=raw.value.trim().startsWith('[')?JSON.parse(raw.value):raw.value.trim().split('\n').map(function(line){var p=line.split('|');return {Title:p[0].trim(),Amount:integer((p[1]||'').trim())};}); }
            catch(error){seed=[];}
        }
        seed.forEach(function (item) {append({Title:item.Title,Amount:item.Amount,DueDay:item.DueDay||days(),AcceptanceCriteria:item.AcceptanceCriteria||'Hoàn thành kết quả bàn giao và phạm vi đã thống nhất trong hợp đồng.'});});
        if(seed.length)mode.value='staged';
        editor.querySelector('[data-plan-fallback]').hidden=true;
        mode.addEventListener('change',function(){if(mode.value==='staged' && !list.children.length){append({DueDay:days()});}render();});
        add.addEventListener('click',function(){append({DueDay:days()});render();list.lastElementChild.querySelector('input').focus();});
        form.addEventListener('input',render);form.addEventListener('change',render);render();
    });
}());
