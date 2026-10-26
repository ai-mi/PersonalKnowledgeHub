import { useState, type SubmitEvent } from 'react'
import { Link } from 'react-router'
import { useCollectionMutations, useCollections, type Collection } from './collectionsApi'

function CollectionRow({ collection }: { collection: Collection }) {
  const { update, remove } = useCollectionMutations()
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(collection.name)
  const [description, setDescription] = useState(collection.description ?? '')

  const save = async (e: SubmitEvent) => {
    e.preventDefault()
    await update.mutateAsync({ id: collection.id, input: { name, description: description.trim() === '' ? null : description } })
    setEditing(false)
  }

  if (editing) {
    return (
      <li>
        <form onSubmit={(e) => void save(e).catch(() => undefined)}>
          <input aria-label="Название" value={name} onChange={(e) => setName(e.target.value)} required />{' '}
          <input aria-label="Описание" value={description} onChange={(e) => setDescription(e.target.value)} />{' '}
          <button type="submit" disabled={update.isPending}>
            Сохранить
          </button>{' '}
          <button type="button" onClick={() => setEditing(false)}>
            Отмена
          </button>
          {update.error && <span role="alert">{update.error.message}</span>}
        </form>
      </li>
    )
  }

  return (
    <li>
      <Link to={`/?collection=${collection.id}`}>{collection.name}</Link> ({collection.itemCount}){' '}
      {collection.description && <small>{collection.description} </small>}
      <button type="button" onClick={() => setEditing(true)}>
        Переименовать
      </button>{' '}
      <button
        type="button"
        onClick={() => {
          if (window.confirm(`Удалить коллекцию «${collection.name}»? Записи останутся.`)) remove.mutate(collection.id)
        }}
      >
        Удалить
      </button>
      {remove.error && <span role="alert">{remove.error.message}</span>}
    </li>
  )
}

export function CollectionsPage() {
  const { data, isPending, error } = useCollections()
  const { create } = useCollectionMutations()
  const [name, setName] = useState('')

  const submit = async (e: SubmitEvent) => {
    e.preventDefault()
    await create.mutateAsync({ name, description: null })
    setName('')
  }

  return (
    <>
      <h1>Коллекции</h1>
      <form onSubmit={(e) => void submit(e).catch(() => undefined)}>
        <label>
          Новая коллекция <input value={name} onChange={(e) => setName(e.target.value)} required />
        </label>{' '}
        <button type="submit" disabled={create.isPending}>
          Создать
        </button>
        {create.error && <span role="alert">{create.error.message}</span>}
      </form>
      {isPending && <p>Загрузка…</p>}
      {error && <p role="alert">{error.message}</p>}
      {data?.length === 0 && <p>Коллекций пока нет.</p>}
      <ul>{data?.map((c) => <CollectionRow key={c.id} collection={c} />)}</ul>
    </>
  )
}
