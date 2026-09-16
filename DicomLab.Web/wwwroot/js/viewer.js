const image = document.getElementById('dicom-image');
if (image) {
    const status = document.getElementById('image-status');
    fetch(image.dataset.src).then(async response => {
        if (!response.ok) {
            const problem = await response.json();
            throw new Error(problem.title || 'No se pudo renderizar la imagen.');
        }
        const url = URL.createObjectURL(await response.blob());
        image.onload = () => { URL.revokeObjectURL(url); status.hidden = true; image.hidden = false; };
        image.onerror = () => { URL.revokeObjectURL(url); status.textContent = 'No se pudo mostrar el PNG.'; };
        image.src = url;
    }).catch(error => { status.textContent = error.message; });
}
