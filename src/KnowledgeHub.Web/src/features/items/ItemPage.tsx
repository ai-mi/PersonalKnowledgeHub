import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { CodeBlock } from '../../shared/CodeBlock'
import { Markdown } from '../../shared/Markdown'
import { ItemForm } from './ItemForm'
import { ItemStatusBadge } from './ItemStatusBadge'
import { UploadForm } from './UploadForm'
import { itemTypeLabels, useItem, useItemMutations, type Item, type ItemInput } from './itemsApi'

const displayTitle = (item: Item) => item.title ?? item.url ?? 'Без названия'

export function NewItemPage() {
  const navigate = useNavigate()
  const { create } = useItemMutations()

  return (
    <>
      <h1>Новая запись</h1>
      <ItemForm
        submitLabel="Сохранить"
        onSubmit={async (input) => {
          const item = await create.mutateAsync(input)
          await navigate(`/items/${item.id}`)
        }}
      />
    </>
  )
}

export function UploadItemPage() {
  const navigate = useNavigate()

  return (
    <>
      <h1>Загрузить документ</h1>
      <UploadForm onUploaded={(item) => void navigate(`/items/${item.id}`)} />
    </>
  )
}

function ItemView({ item }: { item: Item }) {
  return (
    <>
      {item.url && (
        <p>
          <a href={item.url} target="_blank" rel="noopener noreferrer">
            {item.url}
          </a>
        </p>
      )}
      {(item.type === 'link' || item.type === 'document') && (
        <p>
          <ItemStatusBadge item={item} withRetry />
        </p>
      )}
      {item.type === 'document' && (
        <p>
          <a href={`/api/items/${item.id}/file`} download>
            Скачать
          </a>
        </p>
      )}
      {item.userNote && <Markdown>{item.userNote}</Markdown>}
      {item.body &&
        (item.type === 'snippet' ? <CodeBlock code={item.body} language={item.codeLanguage} /> : <Markdown>{item.body}</Markdown>)}
      <p>
        {item.tags.map((tag) => (
          <Link key={tag} to={`/?tag=${encodeURIComponent(tag)}`}>
            #{tag}{' '}
          </Link>
        ))}
      </p>
      {item.collections.length > 0 && (
        <p>
          Коллекции:{' '}
          {item.collections.map((c) => (
            <Link key={c.id} to={`/?collection=${c.id}`}>
              {c.name}{' '}
            </Link>
          ))}
        </p>
      )}
    </>
  )
}

export function ItemPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const [editing, setEditing] = useState(false)
  const { data: item, isPending, error } = useItem(id)
  const { update, setRead, remove } = useItemMutations()

  if (isPending) return <p>Загрузка…</p>
  if (error) return <p role="alert">{error.message}</p>

  const save = async (input: ItemInput) => {
    await update.mutateAsync({ id, input })
    setEditing(false)
  }
  const confirmRemove = async () => {
    if (window.confirm(`Удалить запись «${displayTitle(item)}»?`)) {
      await remove.mutateAsync(id)
      await navigate('/')
    }
  }

  return (
    <>
      <small>{itemTypeLabels[item.type]}</small>
      <h1>{displayTitle(item)}</h1>
      <p>
        <button type="button" onClick={() => setRead.mutate({ id, read: item.readAt === null })} disabled={setRead.isPending}>
          {item.readAt === null ? 'Прочитано' : 'Вернуть в непрочитанное'}
        </button>{' '}
        <button type="button" onClick={() => setEditing((v) => !v)}>
          {editing ? 'Отмена' : 'Редактировать'}
        </button>{' '}
        <button type="button" onClick={() => void confirmRemove()}>
          Удалить
        </button>
      </p>
      {editing ? <ItemForm item={item} submitLabel="Сохранить" onSubmit={save} /> : <ItemView item={item} />}
    </>
  )
}
