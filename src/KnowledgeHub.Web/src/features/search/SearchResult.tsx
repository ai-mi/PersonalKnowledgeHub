import { ItemCard } from '../items/ItemCard'
import { Highlight } from './Highlight'
import type { SearchHit } from './searchApi'

export function SearchResult({ hit }: { hit: SearchHit }) {
  return (
    <div>
      <ItemCard item={hit.item} />
      {hit.highlights.map((fragment, i) => (
        <blockquote key={i}>
          …<Highlight text={fragment} />…
        </blockquote>
      ))}
    </div>
  )
}
