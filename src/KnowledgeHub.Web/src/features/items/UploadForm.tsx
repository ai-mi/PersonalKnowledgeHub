import { useState, type DragEvent, type SubmitEvent } from 'react'
import { ApiError } from '../../api/client'
import { useCollections } from '../collections/collectionsApi'
import { TagInput } from './TagInput'
import { useItemMutations, type Item } from './itemsApi'
import { allowedExtensions, validateFile } from './uploadValidation'

export function UploadForm({ onUploaded }: { onUploaded: (item: Item) => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [title, setTitle] = useState('')
  const [userNote, setUserNote] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [collectionIds, setCollectionIds] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [progress, setProgress] = useState<number | null>(null)
  const { data: collections } = useCollections()
  const { upload } = useItemMutations()

  const choose = (candidate: File | undefined) => {
    if (!candidate) return
    const problem = validateFile(candidate)
    setError(problem)
    setFile(problem ? null : candidate)
  }

  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    choose(e.dataTransfer.files[0])
  }

  const toggleCollection = (id: string) =>
    setCollectionIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))

  const submit = async (e: SubmitEvent) => {
    e.preventDefault()
    if (!file) {
      setError('Выберите файл')
      return
    }
    setError(null)
    setProgress(0)
    try {
      const item = await upload.mutateAsync({
        input: { file, title: title.trim() || null, userNote: userNote.trim() || null, tags, collectionIds },
        onProgress: setProgress,
      })
      onUploaded(item)
    } catch (err) {
      setError(err instanceof ApiError ? (err.errors ? Object.values(err.errors).flat().join(' ') : err.message) : 'Не удалось загрузить файл')
    } finally {
      setProgress(null)
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)}>
      <div onDragOver={(e) => e.preventDefault()} onDrop={onDrop}>
        <label>
          Файл (PDF, DOCX, TXT, MD, до 20 МБ){' '}
          <input type="file" accept={allowedExtensions.join(',')} onChange={(e) => choose(e.target.files?.[0])} />
        </label>
        <p>{file ? `Выбрано: ${file.name}` : 'или перетащите файл сюда'}</p>
      </div>
      <p>
        <label>
          Заголовок <input value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
      </p>
      <p>
        <label>
          Моя заметка
          <br />
          <textarea value={userNote} onChange={(e) => setUserNote(e.target.value)} rows={3} cols={80} />
        </label>
      </p>
      <p>
        Теги: <TagInput value={tags} onChange={setTags} />
      </p>
      {collections && collections.length > 0 && (
        <fieldset>
          <legend>Коллекции</legend>
          {collections.map((c) => (
            <label key={c.id}>
              <input type="checkbox" checked={collectionIds.includes(c.id)} onChange={() => toggleCollection(c.id)} /> {c.name}{' '}
            </label>
          ))}
        </fieldset>
      )}
      <button type="submit" disabled={upload.isPending}>
        Загрузить
      </button>
      {progress !== null && <progress aria-label="Прогресс отправки" value={progress} max={1} />}
      {error && <p role="alert">{error}</p>}
    </form>
  )
}
