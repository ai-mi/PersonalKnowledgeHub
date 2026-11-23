import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { CodeBlock } from '../../shared/CodeBlock'
import { Markdown } from '../../shared/Markdown'
import { ReminderPicker } from '../reminders/ReminderPicker'
import { ItemForm } from './ItemForm'
import { ItemStatusBadge } from './ItemStatusBadge'
import { UploadForm } from './UploadForm'
import { displayTitle, itemTypeLabels, useItem, useItemMutations, type Item, type ItemInput } from './itemsApi'

export function NewItemPage() {
  const navigate = useNavigate()
  const { create } = useItemMutations()

  return (
    <>
      <h1>New item</h1>
      <ItemForm
        submitLabel="Save"
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
      <h1>Upload document</h1>
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
            Download
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
          Collections:{' '}
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

  if (isPending) return <p>Loading…</p>
  if (error) return <p role="alert">{error.message}</p>

  const save = async (input: ItemInput) => {
    await update.mutateAsync({ id, input })
    setEditing(false)
  }
  const confirmRemove = async () => {
    if (window.confirm(`Delete item "${displayTitle(item)}"?`)) {
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
          {item.readAt === null ? 'Mark as read' : 'Mark as unread'}
        </button>{' '}
        <button type="button" onClick={() => setEditing((v) => !v)}>
          {editing ? 'Cancel' : 'Edit'}
        </button>{' '}
        <button type="button" onClick={() => void confirmRemove()}>
          Delete
        </button>
      </p>
      {editing ? <ItemForm item={item} submitLabel="Save" onSubmit={save} /> : <ItemView item={item} />}
      <ReminderPicker itemId={id} />
    </>
  )
}
