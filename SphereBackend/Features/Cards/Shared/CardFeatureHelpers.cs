using AutoMapper;
using SphereBackend.Features.Columns;
using SphereBackend.Models;
using SphereBackend.Services;

namespace SphereBackend.Features.Cards
{
    internal static class CardFeatureHelpers
    {
        public static bool HasDuplicateIds(IEnumerable<int> ids)
        {
            var seen = new HashSet<int>();
            foreach (var id in ids)
            {
                if (!seen.Add(id))
                {
                    return true;
                }
            }

            return false;
        }

        public static void AssignSequentialOrders(IList<Card> cards, DateTime updatedAt, int updatedById)
        {
            for (var index = 0; index < cards.Count; index++)
            {
                var card = cards[index];
                if (card.Order == index)
                {
                    continue;
                }

                card.Order = index;
                card.UpdatedAt = updatedAt;
                card.UpdatedById = updatedById;
            }
        }

        public static CardDto MapToDto(IMapper mapper, Card card)
        {
            return mapper.Map<CardDto>(card);
        }

        public static CardShortDto MapToShortDto(IIdHasher idHasher, Card card)
        {
            return new CardShortDto
            {
                Id = idHasher.Encode(card.Id),
                Title = card.Title,
                Order = card.Order,
                ColumnId = card.ColumnId.HasValue ? idHasher.Encode(card.ColumnId.Value) : null,
                AssigneeId = card.AssigneeId.HasValue ? idHasher.Encode(card.AssigneeId.Value) : null,
                ArchivedAt = card.ArchivedAt
            };
        }

        public static ColumnShortDto MapTargetColumnDto(IIdHasher idHasher, Column column, IEnumerable<Card> cards)
        {
            return new ColumnShortDto
            {
                Id = idHasher.Encode(column.Id),
                Title = column.Title,
                Order = column.Order,
                ArchivedAt = column.ArchivedAt,
                Cards = cards.Select(card => MapToShortDto(idHasher, card)).ToList()
            };
        }
    }
}
