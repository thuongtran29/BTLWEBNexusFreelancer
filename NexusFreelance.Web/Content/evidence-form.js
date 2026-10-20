(function () {
    'use strict';
    var form = document.querySelector('[data-evidence-form]');
    if (!form) return;
    var claim = form.querySelector('#claimId');
    var types = Array.from(form.querySelectorAll('.evidence-type'));
    function render() {
        var option = claim.options[claim.selectedIndex];
        var skill = option ? option.dataset.skillId : '';
        form.querySelectorAll('[data-evidence-block]').forEach(function (block) {
            var type = block.querySelector('.evidence-type').value;
            block.querySelectorAll('[data-evidence-panel]').forEach(function (panel) {
                var enabled = panel.dataset.evidencePanel === type;
                panel.hidden = !enabled;
                panel.querySelectorAll('input,select').forEach(function (input) {
                    input.disabled = !enabled;
                    input.required = enabled && input.hasAttribute('data-required');
                });
            });
            var attempts = block.querySelector('[data-attempt-choice]');
            var available = 0;
            Array.from(attempts.options).forEach(function (item) {
                if (!item.value) return;
                var matches = item.dataset.skillId === skill;
                item.hidden = !matches; item.disabled = !matches;
                if (matches) available++;
            });
            if (attempts.selectedIndex >= 0 && attempts.options[attempts.selectedIndex].disabled) attempts.value = '';
            block.querySelector('[data-empty-attempt]').hidden = available > 0;
        });
    }
    form.addEventListener('change', function () { types.forEach(function (type) { type.setCustomValidity(''); }); render(); });
    form.addEventListener('submit', function (event) {
        if (!types.some(function (type) { return !!type.value; })) {
            event.preventDefault(); types[0].setCustomValidity('Chọn ít nhất một loại bằng chứng để gửi.'); types[0].reportValidity();
        }
    });
    render();
}());
