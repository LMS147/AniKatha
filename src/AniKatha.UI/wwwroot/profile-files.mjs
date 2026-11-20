export async function verifyAvatar(url) {
  const image = new Image();
  image.src = url;
  await image.decode();
  return image.naturalWidth > 0 && image.naturalHeight > 0 &&
    image.naturalWidth <= 4096 && image.naturalHeight <= 4096;
}

export function downloadBackup(json) {
  const link = document.createElement('a');
  const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
  link.href = url;
  link.download = 'anikatha-library.json';
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}
