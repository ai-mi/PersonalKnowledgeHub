import { useState, type SubmitEvent } from 'react'
import { ApiError } from '../../api/client'
import { useCollections } from '../collections/collectionsApi'
import { TagInput } from './TagInput'
import { itemTypeLabels, type Item, type ItemInput, type ItemType } from './itemsApi'

const creatableTypes: ItemType[] = ['note', 'snippet', 'link']

interface ItemFormProps {
  /** Редактируемая запись: тип и адрес ссылки не меняются. */
  item?: Item
  submitLabel: string
  onSubmit: (input: ItemInput) => Promise<void>
}

function FieldError({ errors, field }: { errors: Record<string, string[]>; field: string }) {
  const messages = Object.entries(errors).find(([key]) => key.toLowerCase() === field.toLowerCase())?.[1]
  return messages ? <span role="alert">{messages.join(' ')}</span> : null
}

const blankToNull = (value: string) => (value.trim() === '' ? null : value)

export function ItemForm({ item, submitLabel, onSubmit }: ItemFormProps) {
  const [type, setType] = useState<ItemType>(item?.type ?? 'note')
  const [title, setTitle] = useState(item?.title ?? '')
  const [url, setUrl] = useState(item?.url ?? '')
  const [body, setBody] = useState(item?.body ?? '')
  const [userNote, setUserNote] = useState(item?.userNote ?? '')
  const [codeLanguage, setCodeLanguage] = useState(item?.codeLanguage ?? '')
  const [tags, setTags] = useState(item?.tags ?? [])
  const [collectionIds, setCollectionIds] = useState(item?.collections.map((c) => c.id) ?? [])
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const { data: collections } = useCollections()

  const hasBody = type === 'note' || type === 'snippet'

  const submit = async (e: SubmitEvent) => {
    e.preventDefault()
    setErrors({})
    setFormError(null)
    setPending(true)
    try {
      await onSubmit({
        type,
        title: blankToNull(title),
        url: type === 'link' ? blankToNull(url) : null,
        body: hasBody ? blankToNull(body) : null,
        userNote: blankToNull(userNote),
        codeLanguage: type === 'snippet' ? blankToNull(codeLanguage) : null,
        tags,
        collectionIds,
      })
    } catch (error) {
      if (error instanceof ApiError && error.errors) {
        setErrors(error.errors)
      } else {
        setFormError(error instanceof Error ? error.message : 'Не удалось сохранить')
      }
    } finally {
      setPending(false)
    }
  }

  const toggleCollection = (id: string) =>
    setCollectionIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))

  return (
    <form onSubmit={(e) => void submit(e)}>
      <p>
        <label>
          Тип{' '}
          <select value={type} onChange={(e) => setType(e.target.value as ItemType)} disabled={item !== undefined}>
            {(item ? [item.type] : creatableTypes).map((t) => (
              <option key={t} value={t}>
                {itemTypeLabels[t]}
              </option>
            ))}
          </select>
        </label>
      </p>
      {type === 'link' && (
        <p>
          <label>
            Адрес <input type="url" value={url} onChange={(e) => setUrl(e.target.value)} disabled={item !== undefined} required />
          </label>
          <FieldError errors={errors} field="url" />
        </p>
      )}
      <p>
        <label>
          Заголовок <input value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <FieldError errors={errors} field="title" />
      </p>
      {type === 'snippet' && (
        <p>
          <label>
            Язык <input value={codeLanguage} onChange={(e) => setCodeLanguage(e.target.value)} />
          </label>
          <FieldError errors={errors} field="codeLanguage" />
        </p>
      )}
      {hasBody && (
        <p>
          <label>
            {type === 'note' ? 'Текст (Markdown)' : 'Код'}
            <br />
            <textarea value={body} onChange={(e) => setBody(e.target.value)} rows={10} cols={80} />
          </label>
          <FieldError errors={errors} field="body" />
        </p>
      )}
      <p>
        <label>
          Моя заметка
          <br />
          <textarea value={userNote} onChange={(e) => setUserNote(e.target.value)} rows={3} cols={80} />
        </label>
        <FieldError errors={errors} field="userNote" />
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
      <button type="submit" disabled={pending}>
        {submitLabel}
      </button>
      {formError && <p role="alert">{formError}</p>}
    </form>
  )
}
