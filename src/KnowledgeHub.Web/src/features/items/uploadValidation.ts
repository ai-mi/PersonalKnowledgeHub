export const MAX_UPLOAD_BYTES = 20 * 1024 * 1024
export const allowedExtensions = ['.pdf', '.docx', '.txt', '.md']

/** Client-side validation before upload; the server still re-validates size and signature. */
export function validateFile(file: File): string | null {
  const name = file.name.toLowerCase()
  if (!allowedExtensions.some((ext) => name.endsWith(ext))) {
    return `Allowed formats: ${allowedExtensions.join(', ')}`
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return 'File is larger than 20 MB'
  }
  return null
}
