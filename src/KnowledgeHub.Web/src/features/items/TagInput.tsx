import { useId, useState, type KeyboardEvent } from 'react'
import { useTags } from './itemsApi'

interface TagInputProps {
  value: string[]
  onChange: (tags: string[]) => void
}

/** Теги чипами; Enter или запятая добавляют, автодополнение — из существующих тегов пользователя. */
export function TagInput({ value, onChange }: TagInputProps) {
  const [draft, setDraft] = useState('')
  const listId = useId()
  const { data: known } = useTags()

  const add = () => {
    const name = draft.trim().replace(/^#+/, '')
    if (name !== '' && !value.some((t) => t.toLowerCase() === name.toLowerCase())) {
      onChange([...value, name])
    }
    setDraft('')
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault()
      add()
    } else if (e.key === 'Backspace' && draft === '' && value.length > 0) {
      onChange(value.slice(0, -1))
    }
  }

  return (
    <span>
      {value.map((tag) => (
        <button key={tag} type="button" aria-label={`Убрать тег ${tag}`} onClick={() => onChange(value.filter((t) => t !== tag))}>
          #{tag} ×
        </button>
      ))}{' '}
      <input
        aria-label="Теги"
        list={listId}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={onKeyDown}
        onBlur={add}
        placeholder="Добавить тег"
      />
      <datalist id={listId}>
        {known?.map((tag) => (
          <option key={tag.id} value={tag.name} />
        ))}
      </datalist>
    </span>
  )
}
