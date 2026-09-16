document.querySelectorAll('[data-operation-form]').forEach(form => {
    form.addEventListener('submit', () => {
        form.querySelector('button[type="submit"]').disabled = true;
        form.querySelector('[data-operation-status]').textContent = 'Operación en curso. Esperando respuesta DICOM…';
    });
});
