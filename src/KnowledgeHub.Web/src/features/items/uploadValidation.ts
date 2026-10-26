export const MAX_UPLOAD_BYTES = 20 * 1024 * 1024
export const allowedExtensions = ['.pdf', '.docx', '.txt', '.md']

/** Проверка до отправки; сервер всё равно перепроверяет размер и сигнатуру. */
export function validateFile(file: File): string | null {
  const name = file.name.toLowerCase()
  if (!allowedExtensions.some((ext) => name.endsWith(ext))) {
    return `Допустимые форматы: ${allowedExtensions.join(', ')}`
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return 'Файл больше 20 МБ'
  }
  return null
}
