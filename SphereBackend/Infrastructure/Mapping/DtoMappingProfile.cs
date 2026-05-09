using AutoMapper;
using SphereBackend.Features.Boards;
using SphereBackend.Features.Cards;
using SphereBackend.Features.Columns;
using SphereBackend.Models;
using SphereBackend.Services;

namespace SphereBackend.Mappings
{
    public class DtoMappingProfile : Profile
    {
        public DtoMappingProfile()
        {
            CreateMap<Board, BoardShortDto>()
                .ForMember(d => d.Id, o => o.ConvertUsing<IntToHashStringConverter, int>(s => s.Id))
                .ForMember(d => d.IsArchived, o => o.MapFrom(s => s.ArchivedAt != null));

            CreateMap<Column, ColumnShortDto>()
                .ForMember(d => d.Id, o => o.ConvertUsing<IntToHashStringConverter, int>(s => s.Id));

            CreateMap<Column, ArchivedColumnShortDto>()
                .ForMember(d => d.Id, o => o.ConvertUsing<IntToHashStringConverter, int>(s => s.Id));

            CreateMap<Card, CardDto>()
                .ForMember(d => d.Id, o => o.ConvertUsing<IntToHashStringConverter, int>(s => s.Id))
                .ForMember(d => d.ColumnId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.ColumnId))
                .ForMember(d => d.BoardId, o => o.ConvertUsing<IntToHashStringConverter, int>(s => s.BoardId))
                .ForMember(d => d.AssigneeId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.AssigneeId))
                .ForMember(d => d.PreviousColumnId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.PreviousColumnId));

            CreateMap<Card, CardShortDto>()
                .ForMember(d => d.Id, o => o.ConvertUsing<IntToHashStringConverter, int>(s => s.Id))
                .ForMember(d => d.ColumnId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.ColumnId))
                .ForMember(d => d.AssigneeId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.AssigneeId));

            CreateMap<Card, ArchivedCardDto>()
                .ForMember(d => d.Id, o => o.ConvertUsing<IntToHashStringConverter, int>(s => s.Id))
                .ForMember(d => d.ColumnId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.ColumnId))
                .ForMember(d => d.PreviousColumnId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.PreviousColumnId))
                .ForMember(d => d.AssigneeId, o => o.ConvertUsing<NullableIntToHashStringConverter, int?>(s => s.AssigneeId));
        }
    }

    public class IntToHashStringConverter : IValueConverter<int, string>
    {
        private readonly IIdHasher _idHasher;

        public IntToHashStringConverter(IIdHasher idHasher)
        {
            _idHasher = idHasher;
        }

        public string Convert(int sourceMember, ResolutionContext context)
        {
            return _idHasher.Encode(sourceMember);
        }
    }

    public class NullableIntToHashStringConverter : IValueConverter<int?, string?>
    {
        private readonly IIdHasher _idHasher;

        public NullableIntToHashStringConverter(IIdHasher idHasher)
        {
            _idHasher = idHasher;
        }

        public string? Convert(int? sourceMember, ResolutionContext context)
        {
            return sourceMember.HasValue ? _idHasher.Encode(sourceMember.Value) : null;
        }
    }
}
