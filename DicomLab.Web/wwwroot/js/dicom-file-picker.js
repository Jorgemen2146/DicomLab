document.querySelectorAll('[data-dicom-picker]').forEach(picker => {
    const input = picker.querySelector('input[type="file"]');
    const name = picker.querySelector('[data-dicom-file-name]');
    input.addEventListener('change', () => {
        name.textContent = input.files[0]?.name || 'Ningún archivo seleccionado';
    });
});
