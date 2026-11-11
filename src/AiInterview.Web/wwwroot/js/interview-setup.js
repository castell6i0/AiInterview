'use strict';
(() => {
    const form = document.getElementById('createInterviewForm');
    if (!form) return;
    const field = name => document.getElementById(name);
    const label = name => field(name).selectedOptions[0]?.textContent.trim() || '';
    const kinds = [
        'Ôn kiến thức chuyên môn qua câu hỏi và tình huống thực tế.',
        'Luyện cách kể một tình huống, hành động của bạn và kết quả đạt được.',
        'Trao đổi về dự án trong CV. Hãy chọn CV ở phần cá nhân hóa bên dưới.',
        'Bám sát yêu cầu công việc. Hãy chọn tin tuyển dụng ở phần cá nhân hóa bên dưới.',
        'Kết hợp câu hỏi chuyên môn và cách xử lý tình huống.',
        'Viết code trong trình soạn thảo và nhận góp ý. Code được AI review, không thực thi.'
    ];
    function update() {
        const kind = Number(field('Kind').value);
        field('summaryPosition').textContent = field('PositionId').value ? label('PositionId') : 'Chưa chọn vị trí';
        field('summaryKind').textContent = label('Kind');
        field('summaryPreferences').textContent = `${label('Level')} · ${label('Language')}`;
        field('summaryMode').textContent = label('Mode');
        field('summaryCount').textContent = label('QuestionCount');
        field('kindHelp').textContent = kinds[kind] || kinds[0];
        field('modeHelp').textContent = field('Mode').value === '1'
            ? 'Bấm microphone để nói, kiểm tra bản chép lời rồi gửi. Bạn luôn có thể chuyển sang gõ.'
            : 'Bạn gõ câu trả lời và gửi khi đã sẵn sàng.';
        field('CvProfileId').required = kind === 2;
        field('JobDescriptionId').required = kind === 3;
        if (kind === 2 || kind === 3) field('personalization').open = true;
    }
    form.addEventListener('change', update);
    // Keep browser validation messages reachable even if the optional section was collapsed.
    form.addEventListener('invalid', event => {
        if (field('personalization').contains(event.target)) field('personalization').open = true;
    }, true);
    update();
})();
