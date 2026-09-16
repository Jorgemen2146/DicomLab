const dicomFiles = document.getElementById('Files');
const selectedDicoms = document.getElementById('selected-dicoms');
dicomFiles.addEventListener('change', () => {
    selectedDicoms.replaceChildren();
    for (const file of dicomFiles.files) {
        const item = document.createElement('li');
        item.textContent = file.name;
        selectedDicoms.appendChild(item);
    }
});
