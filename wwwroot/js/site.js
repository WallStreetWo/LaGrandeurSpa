(() => {
    document.querySelectorAll('.spa-toast-message:not(.static-message)').forEach(el => {
        setTimeout(() => {
            el.style.transition = 'opacity .35s ease, transform .35s ease';
            el.style.opacity = '0';
            el.style.transform = 'translateY(-5px)';
            setTimeout(() => el.remove(), 400);
        }, 9000);
    });

    document.querySelectorAll('.js-password-toggle').forEach(button => {
        button.addEventListener('click', () => {
            const targetId = button.dataset.passwordTarget;
            const input = targetId ? document.getElementById(targetId) : null;
            if (!input) return;

            const shouldShow = input.type === 'password';
            input.type = shouldShow ? 'text' : 'password';

            const icon = button.querySelector('i');
            if (icon) {
                icon.classList.toggle('fa-eye', !shouldShow);
                icon.classList.toggle('fa-eye-slash', shouldShow);
            }

            const label = shouldShow ? 'Hide password' : 'Show password';
            button.setAttribute('aria-label', label);
            button.setAttribute('title', label);
        });
    });
})();
