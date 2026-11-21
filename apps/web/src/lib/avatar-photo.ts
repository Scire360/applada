export const avatarMaxBytes = 1_000_000;
export const avatarSide = 192;
export const avatarPixelBytes = avatarSide * avatarSide * 4;

export type AvatarThumbnail = { preview: string; pixels: Uint8Array<ArrayBuffer> };

function isSupportedImage(bytes: Uint8Array) {
  const matches = (offset: number, signature: number[]) =>
    signature.every((value, index) => bytes[offset + index] === value);
  return matches(0, [0xff, 0xd8, 0xff])
    || matches(0, [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
    || (matches(0, [0x52, 0x49, 0x46, 0x46]) && matches(8, [0x57, 0x45, 0x42, 0x50]));
}

export async function makeAvatarThumbnail(file: File): Promise<AvatarThumbnail> {
  if (file.size > avatarMaxBytes) {
    throw new Error("A foto deve ter até 1 MB.");
  }

  if (!isSupportedImage(new Uint8Array(await file.slice(0, 12).arrayBuffer()))) {
    throw new Error("Escolha uma imagem JPG, PNG ou WebP.");
  }

  const bitmap = await createImageBitmap(file).catch(() => {
    throw new Error("Não foi possível abrir a imagem. Escolha outro arquivo.");
  });
  try {
    const side = Math.min(bitmap.width, bitmap.height);
    const canvas = document.createElement("canvas");
    canvas.width = avatarSide;
    canvas.height = avatarSide;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("Não foi possível preparar a foto neste navegador.");
    context.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, avatarSide, avatarSide);
    return {
      preview: canvas.toDataURL("image/png"),
      pixels: new Uint8Array(context.getImageData(0, 0, avatarSide, avatarSide).data),
    };
  } finally {
    bitmap.close();
  }
}
