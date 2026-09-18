using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Ged.Core.Ports.FileTypes;

namespace Ged.Adapters.FileTypes;

/// <summary>Identifie un contenu à partir de ses octets, sans aucune dépendance tierce.</summary>
/// <remarks>
/// <para>
/// Couvre les formats que cette application accepte, et rien d'autre. C'est tout l'intérêt : un
/// détecteur conçu pour une liste fermée de formats autorisés n'a pas la même mission qu'un détecteur
/// conçu pour identifier n'importe quoi — le premier répond à « est-ce bien ce que ça prétend être ? »,
/// le second à « qu'est-ce que c'est que ça ? », et seul le premier est nécessaire pour valider un
/// téléversement.
/// </para>
/// <para>
/// Trois techniques, parce que trois familles de formats en exigent trois :
/// </para>
/// <code>
/// octets de tête     pdf, png, jpeg, gif, tiff, bmp, rtf, exe, elf
/// conteneur ZIP      docx, xlsx, pptx (et leurs jumeaux avec macros), odt, ods, odp
/// conteneur OLE2     doc, xls, ppt — une seule signature, que .msi partage aussi
/// </code>
/// <para>
/// Deux signatures de tête ne font que deux octets — <c>BM</c> et <c>MZ</c> — et sont confirmées par
/// la structure qui les suit avant d'être signalées ; sinon, un texte ordinaire commençant par ces
/// lettres serait refusé comme image ou comme programme.
/// </para>
/// <para>
/// Les exécutables sont reconnus volontairement, même si aucune politique ne les accepte. Identifier
/// un binaire renommé produit « ceci est un EXE » plutôt que « non reconnu », ce qui donne à la fois
/// un meilleur message et un contrôle plus fort pour les formats texte, dont la condition
/// d'acceptation est justement que rien ne soit reconnu.
/// </para>
/// <para>
/// Les lectures de taille fixe passent par <see cref="ReadInto"/>, qui écrit dans un tampon fourni
/// par l'appelant — souvent sur la pile. Seules les lectures de taille variable, dont la taille vient
/// du fichier lui-même, allouent.
/// </para>
/// </remarks>
public sealed class BuiltInContentFormatDetector : IContentFormatDetector
{
    private const int HeaderLength = 512;

    /// <summary>Jusqu'où, dans un fichier, le pointeur d'en-tête Windows peut envoyer le lecteur.</summary>
    /// <remarks>
    /// Les éditeurs de liens le placent dans le premier kilo-octet. La borne empêche un pointeur
    /// falsifié de transformer une lecture de quatre octets en un déplacement à travers tout le
    /// fichier téléversé.
    /// </remarks>
    private const int MaxExecutableHeaderOffset = 64 * 1024;

    /// <summary>Le nombre maximal de secteurs de répertoire d'un fichier composé suivis.</summary>
    /// <remarks>
    /// Les documents Office tiennent leur répertoire en un à quelques secteurs. Le parcours d'une
    /// chaîne à travers une entrée hostile est borné ici, et une boucle s'arrête à la même borne.
    /// </remarks>
    private const int MaxCompoundDirectorySectors = 64;

    /// <summary>Le nombre maximal de secteurs DIFAT parcourus pour localiser un secteur de table d'allocation.</summary>
    private const int MaxCompoundDifatHops = 256;

    /// <summary>Le nombre maximal d'entrées qu'un ZIP peut déclarer avant de ne plus être un document Office candidat.</summary>
    /// <remarks>
    /// Les paquets réels contiennent de quelques dizaines à quelques milliers de parties. La borne
    /// existe parce que la lecture d'un répertoire central coûte de la mémoire par entrée : sans elle,
    /// 16 Mo d'entrées vides coûtent 112 Mo à identifier, et un téléversement de 256 Mo plusieurs
    /// gigaoctets.
    /// </remarks>
    private const int MaxZipEntries = 10_000;

    /// <summary>La taille maximale du répertoire central lu, en octets.</summary>
    /// <remarks>
    /// Borne ce que <see cref="MaxZipEntries"/> ne peut pas borner : un seul nom d'entrée peut faire
    /// 64 Ko.
    /// </remarks>
    private const int MaxCentralDirectoryLength = 2 * 1024 * 1024;

    /// <summary>La taille maximale de <c>[Content_Types].xml</c> lue, compressée ou non.</summary>
    /// <remarks>
    /// Quelques kilo-octets en pratique, quelques dizaines pour une présentation de plusieurs
    /// centaines de diapositives. La borne s'applique à la sortie décompressée, si bien qu'un
    /// manifeste dont les données se décompressent au-delà est abandonné à cette longueur. Deflate
    /// plafonne à environ 1030 pour 1 : 256 Ko de sortie bornent donc aussi bien l'inflation d'un
    /// manifeste compressé que le coût mémoire d'un lecteur XML qui le chargerait en entier.
    /// </remarks>
    private const int MaxManifestLength = 256 * 1024;

    private const int EndOfCentralDirectoryLength = 22;
    private const int CentralHeaderLength = 46;
    private const int LocalHeaderLength = 30;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const int Zip64LocatorLength = 20;
    private const int Zip64EndOfCentralDirectoryLength = 56;
    private const ushort Zip64ExtraFieldId = 0x0001;
    private const ushort Sentinel16 = 0xFFFF;
    private const uint Sentinel32 = 0xFFFFFFFF;
    private const ushort Stored = 0;
    private const ushort Deflated = 8;
    private const ushort EncryptedFlag = 0x0001;

    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint MaxRegularSector = 0xFFFFFFFA;
    private const uint NoStream = 0xFFFFFFFF;
    private const int DirectoryEntryLength = 128;
    private const int HeaderDifatSlots = 109;
    private const int HeaderDifatOffset = 0x4C;

    private const string ManifestName = "[Content_Types].xml";
    private const string OpenDocumentMimeTypeName = "mimetype";

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly DetectedFormat GenericZip = new("zip", "application/zip");
    private static readonly DetectedFormat GenericCompoundFile = new("ole2", "application/x-ole-storage");

    private static readonly DetectedFormat LegacyWord = new("doc", "application/msword");
    private static readonly DetectedFormat LegacyExcel = new("xls", "application/vnd.ms-excel");
    private static readonly DetectedFormat LegacyPowerPoint = new("ppt", "application/vnd.ms-powerpoint");

    /// <summary>Tailles d'en-tête DIB qu'un bitmap peut porter, de BITMAPCOREHEADER (12) à BITMAPV5HEADER (124).</summary>
    private static readonly HashSet<uint> BitmapInfoHeaderSizes = [12, 16, 40, 52, 56, 64, 108, 124];

    private static readonly (byte[] Signature, string Extension, string MediaType, SignatureConfirmation? Confirm)[] Leading =
    [
        ([0x25, 0x50, 0x44, 0x46], "pdf", "application/pdf", null),                       // %PDF
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "png", "image/png", null),
        ([0xFF, 0xD8, 0xFF], "jpg", "image/jpeg", null),
        ([0x47, 0x49, 0x46, 0x38, 0x37, 0x61], "gif", "image/gif", null),                 // GIF87a
        ([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "gif", "image/gif", null),                 // GIF89a
        ([0x49, 0x49, 0x2A, 0x00], "tif", "image/tiff", null),                            // II*
        ([0x4D, 0x4D, 0x00, 0x2A], "tif", "image/tiff", null),                            // MM*
        ([0x42, 0x4D], "bmp", "image/bmp", IsBitmap),                                     // BM
        ([0x7B, 0x5C, 0x72, 0x74, 0x66], "rtf", "application/rtf", null),                 // {\rtf
        ([0x4D, 0x5A], "exe", "application/vnd.microsoft.portable-executable", IsWindowsExecutable), // MZ
        ([0x7F, 0x45, 0x4C, 0x46], "elf", "application/x-elf", null),                     // .ELF
    ];

    private static readonly Dictionary<string, DetectedFormat> OpenDocumentTypes = new(StringComparer.Ordinal)
    {
        ["application/vnd.oasis.opendocument.text"] =
            new("odt", "application/vnd.oasis.opendocument.text"),
        ["application/vnd.oasis.opendocument.spreadsheet"] =
            new("ods", "application/vnd.oasis.opendocument.spreadsheet"),
        ["application/vnd.oasis.opendocument.presentation"] =
            new("odp", "application/vnd.oasis.opendocument.presentation"),
    };

    /// <summary>Types de contenu de la partie principale, selon la famille qu'ils identifient.</summary>
    /// <remarks>
    /// Les modèles, diaporamas et compléments sont absents volontairement : ils retombent sur un ZIP
    /// ordinaire et sont refusés en tant que tel, plutôt que d'être présentés comme le type de
    /// document auquel ils ressemblent.
    /// </remarks>
    private static readonly Dictionary<string, OfficeFamily> MainPartTypes = new(StringComparer.Ordinal)
    {
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"] = OfficeFamily.Word,
        ["application/vnd.ms-word.document.macroEnabled.main+xml"] = OfficeFamily.Word,
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"] = OfficeFamily.Excel,
        ["application/vnd.ms-excel.sheet.macroEnabled.main+xml"] = OfficeFamily.Excel,
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"] = OfficeFamily.PowerPoint,
        ["application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml"] = OfficeFamily.PowerPoint,
    };

    /// <summary>Confirme une signature trop courte pour signifier quoi que ce soit à elle seule.</summary>
    /// <param name="header">Les octets de tête déjà lus.</param>
    /// <param name="content">Le contenu complet, avec positionnement possible, pour un contrôle qui va au-delà de l'en-tête.</param>
    /// <returns>Vrai lorsque la structure attendue derrière la signature est réellement présente.</returns>
    private delegate bool SignatureConfirmation(ReadOnlySpan<byte> header, Stream content);

    private enum OfficeFamily
    {
        Word,
        Excel,
        PowerPoint,
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Le flux ne permet pas le positionnement (seek).</exception>
    /// <remarks>
    /// Un flux sans positionnement est refusé plutôt que lu depuis sa position courante : le contrat
    /// du port en exige un, et une réponse calculée à partir du milieu d'un fichier est pire que
    /// pas de réponse du tout.
    /// </remarks>
    public DetectedFormat? Detect(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanSeek)
        {
            throw new NotSupportedException("Content detection needs a seekable stream.");
        }

        content.Position = 0;

        Span<byte> buffer = stackalloc byte[HeaderLength];
        int read = content.ReadAtLeast(buffer, HeaderLength, throwOnEndOfStream: false);
        ReadOnlySpan<byte> header = buffer[..read];

        // Les conteneurs d'abord : leurs signatures sont partagées, donc une correspondance ici
        // signifie « regarder à l'intérieur », jamais « terminé ».
        if (header.StartsWith(Zip))
        {
            return DetectZipFlavour(content, header);
        }

        if (header.StartsWith(Ole2))
        {
            return DetectCompoundFileFlavour(content, header);
        }

        foreach ((byte[] signature, string extension, string mediaType, SignatureConfirmation? confirm) in Leading)
        {
            if (header.StartsWith(signature) && (confirm is null || confirm(header, content)))
            {
                return new DetectedFormat(extension, mediaType);
            }
        }

        // Le texte brut, le CSV et le JSON arrivent ici, et c'est correct : ils ne portent aucune
        // signature, et dire « rien ne correspond » est plus honnête que deviner.
        return null;
    }

    /// <summary>Confirme un bitmap derrière la signature de deux lettres <c>BM</c>.</summary>
    /// <remarks>
    /// <c>BM</c> seul, ce ne sont que deux lettres ASCII, et un CSV commençant par <c>BMI,</c> était
    /// auparavant signalé comme image — et refusé en tant que texte. Un vrai bitmap a des octets
    /// réservés à zéro, une taille d'en-tête DIB connue, et des données de pixels qui commencent après
    /// les deux en-têtes et à l'intérieur du fichier. Un texte ne satisfait aucune des trois
    /// conditions. La taille de fichier déclarée est laissée au catalogue, qui la vérifie déjà.
    /// </remarks>
    private static bool IsBitmap(ReadOnlySpan<byte> header, Stream content)
    {
        if (header.Length < 18)
        {
            return false;
        }

        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(header[6..]);
        uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[10..]);
        uint infoHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);

        return reserved == 0
            && BitmapInfoHeaderSizes.Contains(infoHeaderSize)
            && pixelOffset >= 14 + infoHeaderSize
            && pixelOffset <= content.Length;
    }

    /// <summary>Confirme un exécutable Windows derrière la signature de deux lettres <c>MZ</c>.</summary>
    /// <remarks>
    /// <para>
    /// L'en-tête DOS pointe, à l'offset 0x3C, vers l'en-tête réel, dont la signature est
    /// <c>PE\0\0</c> pour tout binaire Win32 et .NET, et <c>NE</c>, <c>LE</c> ou <c>LX</c> pour les
    /// plus anciens. Un texte commençant par <c>MZ</c> a un pointeur ASCII qui ne tombe sur rien de
    /// tel.
    /// </para>
    /// <para>
    /// Un exécutable DOS nu, sans en-tête étendu, n'est pas identifié. Il reste refusé en tant que
    /// texte — son en-tête DOS est rempli d'octets nuls, que l'heuristique texte rejette — et refusé
    /// en tant que tout autre chose, car il ne correspond à aucun format accepté.
    /// </para>
    /// </remarks>
    private static bool IsWindowsExecutable(ReadOnlySpan<byte> header, Stream content)
    {
        if (header.Length < 0x40)
        {
            return false;
        }

        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(header[0x3C..]);

        if (offset < 0x40 || offset > MaxExecutableHeaderOffset)
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[4];

        if (offset + 4 <= header.Length)
        {
            header.Slice((int)offset, 4).CopyTo(signature);
        }
        else if (!ReadInto(content, offset, signature))
        {
            return false;
        }

        return signature.SequenceEqual("PE\0\0"u8)
            || signature[..2].SequenceEqual("NE"u8)
            || signature[..2].SequenceEqual("LE"u8)
            || signature[..2].SequenceEqual("LX"u8);
    }

    /// <summary>Distingue les formats qui partagent la signature ZIP.</summary>
    /// <remarks>
    /// <para>
    /// <c>PK\x03\x04</c> identifie un ZIP et rien de plus. Chaque réponse ci-dessous est soit un
    /// format précis prouvé par la structure de l'archive, soit <c>zip</c> — jamais une exception,
    /// jamais une supposition. <c>zip</c> est un format reconnu qu'aucune politique n'accepte : tout
    /// doute aboutit donc à un refus.
    /// </para>
    /// <para>
    /// <see cref="ZipArchive"/> n'est volontairement pas utilisé. Il matérialise toutes les entrées du
    /// répertoire central avant de répondre quoi que ce soit, et ignore le nombre d'entrées déclaré
    /// par l'archive — mesuré : une archive déclarant 10 entrées mais en contenant 200 000 est lue en
    /// entier, 112 Mo, avant de lever une exception. Lire le répertoire ici, avec des bornes strictes,
    /// maintient l'identification à coût constant.
    /// </para>
    /// <para>
    /// Le répertoire central fait foi, parce que c'est lui que lisent Office et LibreOffice. Les
    /// en-têtes locaux ne sont consultés que pour localiser les données.
    /// </para>
    /// </remarks>
    private static DetectedFormat DetectZipFlavour(Stream content, ReadOnlySpan<byte> header)
    {
        if (ReadCentralDirectory(content) is not { } entries)
        {
            return GenericZip;
        }

        if (entries.Count > 0 && entries[0].Name == OpenDocumentMimeTypeName)
        {
            return DetectOpenDocument(entries[0], header) ?? GenericZip;
        }

        return DetectOfficeOpenXml(content, entries) ?? GenericZip;
    }

    /// <summary>Identifie un paquet OpenDocument à partir de sa première entrée.</summary>
    /// <remarks>
    /// La spécification OpenDocument rend ce test exact : la première entrée s'appelle
    /// <c>mimetype</c>, est stockée sans compression, et contient le type de média tel quel. Les
    /// trois conditions sont requises, de sorte que la réponse provient de l'en-tête déjà lu — sans
    /// déplacement, sans décompression. Un paquet qui déroge à l'une des trois n'est pas traité comme
    /// OpenDocument, quel que soit son contenu.
    /// </remarks>
    private static DetectedFormat? DetectOpenDocument(ZipEntryRecord mimetype, ReadOnlySpan<byte> header)
    {
        if (mimetype.LocalHeaderOffset != 0
            || mimetype.Method != Stored
            || mimetype.CompressedSize != mimetype.UncompressedSize
            || mimetype.UncompressedSize > 128
            || header.Length < LocalHeaderLength)
        {
            return null;
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
        int dataStart = LocalHeaderLength + nameLength + extraLength;
        int dataEnd = dataStart + (int)mimetype.UncompressedSize;

        if (BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) != Stored
            || dataEnd > header.Length
            || !header.Slice(LocalHeaderLength, nameLength).SequenceEqual("mimetype"u8))
        {
            return null;
        }

        string mediaType = Encoding.ASCII.GetString(header[dataStart..dataEnd]);

        return OpenDocumentTypes.GetValueOrDefault(mediaType);
    }

    /// <summary>Identifie un paquet Office Open XML à partir de son manifeste de types de contenu.</summary>
    /// <remarks>
    /// <para>
    /// C'est le manifeste, et non les noms des parties, qui décide. Office choisit d'exécuter ou non
    /// les macros d'après le type de contenu de la partie principale : c'est donc ce fait qu'un
    /// contrôle des macros doit lire ; et la partie principale peut légitimement s'appeler
    /// <c>document2.xml</c>, ce qui met en échec un contrôle fondé sur les seuls noms.
    /// </para>
    /// <para>
    /// Tout signal VBA — un type de contenu avec macros, le type de contenu du projet VBA, ou une
    /// partie nommée <c>vbaProject.bin</c> — transforme la réponse en son jumeau avec macros. C'est
    /// plus strict qu'Office, qui ignore une partie VBA sous un type de contenu sans macros, et c'est
    /// délibéré.
    /// </para>
    /// <para>
    /// Les noms de parties sont indexés une fois avant la boucle. Un manifeste peut déclarer autant
    /// de surcharges que l'archive a d'entrées, et confirmer chacune par un balayage de la liste
    /// rendait l'identification quadratique sur un paquet volumineux.
    /// </para>
    /// </remarks>
    private static DetectedFormat? DetectOfficeOpenXml(Stream content, List<ZipEntryRecord> entries)
    {
        ZipEntryRecord? manifest = entries.Find(e => e.Name == ManifestName);

        if (manifest is null || ReadManifest(content, manifest) is not { } contentTypes)
        {
            return null;
        }

        OfficeFamily? family = ResolveOfficeFamily(entries, contentTypes);

        if (family is not { } resolved)
        {
            return null;
        }

        bool hasMacros = contentTypes.Exists(
                t => t.ContentType.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)
                     || t.ContentType.Equals("application/vnd.ms-office.vbaProject", StringComparison.OrdinalIgnoreCase))
            || entries.Exists(e => e.Name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase));

        return (resolved, hasMacros) switch
        {
            (OfficeFamily.Word, false) => new(
                "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            (OfficeFamily.Word, true) => new(
                "docm", "application/vnd.ms-word.document.macroEnabled.12"),
            (OfficeFamily.Excel, false) => new(
                "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            (OfficeFamily.Excel, true) => new(
                "xlsm", "application/vnd.ms-excel.sheet.macroEnabled.12"),
            (OfficeFamily.PowerPoint, false) => new(
                "pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            (OfficeFamily.PowerPoint, true) => new(
                "pptm", "application/vnd.ms-powerpoint.presentation.macroEnabled.12"),
            _ => null,
        };
    }

    /// <summary>Résout la famille Office déclarée par le manifeste, ou null lorsqu'aucune ne l'est.</summary>
    /// <remarks>
    /// Une partie principale que le manifeste nomme mais que l'archive ne contient pas ne prouve rien,
    /// et deux parties principales de familles différentes ne font pas un document que l'on puisse
    /// nommer.
    /// </remarks>
    private static OfficeFamily? ResolveOfficeFamily(
        List<ZipEntryRecord> entries,
        List<(string? PartName, string ContentType)> contentTypes)
    {
        HashSet<string>? partNames = null;
        OfficeFamily? family = null;

        foreach ((string? partName, string contentType) in contentTypes)
        {
            if (partName is null || !MainPartTypes.TryGetValue(contentType, out OfficeFamily found))
            {
                continue;
            }

            partNames ??= BuildPartNames(entries);

            if (!partNames.Contains(partName))
            {
                continue;
            }

            if (family is not null && family != found)
            {
                return null;
            }

            family = found;
        }

        return family;

        static HashSet<string> BuildPartNames(List<ZipEntryRecord> entries)
        {
            var names = new HashSet<string>(entries.Count, StringComparer.OrdinalIgnoreCase);

            foreach (ZipEntryRecord entry in entries)
            {
                names.Add("/" + entry.Name);
            }

            return names;
        }
    }

    /// <summary>Lit le répertoire central, ou renvoie null lorsqu'il n'est pas digne de confiance.</summary>
    /// <remarks>
    /// <para>
    /// Le ZIP64 est lu, pas refusé. La spécification l'autorise même lorsqu'aucune limite n'est
    /// dépassée, et certains outils l'utilisent de toute façon — Info-ZIP dès que son entrée est
    /// lue en flux, DotNetZip sur chaque entrée. Un document réempaqueté par l'un ou l'autre est
    /// légitime : les valeurs 64 bits sont donc résolues, puis soumises aux mêmes bornes que tout le
    /// reste.
    /// </para>
    /// <para>
    /// Tout autre doute structurel renvoie null plutôt que d'être réparé : une archive multi-volumes,
    /// des données avant la première entrée, des octets après l'enregistrement de fin, des
    /// enregistrements 32 bits et 64 bits en désaccord, un nombre d'entrées ou une taille de
    /// répertoire au-delà des bornes, un en-tête qui déborde du répertoire. Les documents Office sont
    /// des fichiers uniques écrits en une seule passe ; les archives qui ne le sont pas ne sont pas ce
    /// qu'un document Office prétend être.
    /// </para>
    /// <para>
    /// Rejeter les données situées avant la première entrée ferme aussi la forme auto-extractible, où
    /// un ZIP se cache derrière un préfixe qu'un autre lecteur prendrait pour le vrai fichier.
    /// </para>
    /// </remarks>
    private static List<ZipEntryRecord>? ReadCentralDirectory(Stream content)
    {
        long length = content.Length;
        int tailLength = (int)Math.Min(length, EndOfCentralDirectoryLength + ushort.MaxValue);

        if (ReadAt(content, length - tailLength, tailLength) is not { } tail)
        {
            return null;
        }

        int eocd = FindEndRecord(tail);

        if (eocd < 0)
        {
            return null;
        }

        long eocdPosition = length - tailLength + eocd;
        EndRecord classic = ReadClassicEndRecord(tail.AsSpan(eocd), eocdPosition);
        EndRecord? end = ResolveEndRecord(content, eocdPosition, classic);

        if (end is null || !IsUsable(end))
        {
            return null;
        }

        long directoryOffset = (long)end.DirectoryOffset;

        if (ReadAt(content, directoryOffset, (int)end.DirectorySize) is not { } directory)
        {
            return null;
        }

        return ParseEntries(directory, (int)end.TotalEntries, directoryOffset);
    }

    /// <summary>Localise l'enregistrement de fin, ou -1 lorsqu'il n'y en a pas d'exploitable.</summary>
    /// <remarks>
    /// L'enregistrement est recherché depuis la fin, et son commentaire doit s'étendre exactement
    /// jusqu'à la fin du fichier : une signature plantée dans le commentaire ne peut pas le masquer,
    /// et rien ne peut le suivre.
    /// </remarks>
    private static int FindEndRecord(ReadOnlySpan<byte> tail)
    {
        for (int i = tail.Length - EndOfCentralDirectoryLength; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail[i..]) == EndOfCentralDirectorySignature
                && i + EndOfCentralDirectoryLength + BinaryPrimitives.ReadUInt16LittleEndian(tail[(i + 20)..]) == tail.Length)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Lit les champs de l'enregistrement de fin 32 bits.</summary>
    private static EndRecord ReadClassicEndRecord(ReadOnlySpan<byte> record, long eocdPosition) =>
        new(
            Disk: BinaryPrimitives.ReadUInt16LittleEndian(record[4..]),
            DirectoryDisk: BinaryPrimitives.ReadUInt16LittleEndian(record[6..]),
            EntriesOnDisk: BinaryPrimitives.ReadUInt16LittleEndian(record[8..]),
            TotalEntries: BinaryPrimitives.ReadUInt16LittleEndian(record[10..]),
            DirectorySize: BinaryPrimitives.ReadUInt32LittleEndian(record[12..]),
            DirectoryOffset: BinaryPrimitives.ReadUInt32LittleEndian(record[16..]),
            End: (ulong)eocdPosition);

    /// <summary>Choisit entre l'enregistrement 32 bits et son homologue ZIP64.</summary>
    /// <remarks>
    /// Un localisateur présent mais refusé n'autorise pas de repli sur les champs étroits : l'archive
    /// a déclaré des valeurs 64 bits, et lire les autres décrirait une autre archive.
    /// </remarks>
    private static EndRecord? ResolveEndRecord(Stream content, long eocdPosition, EndRecord classic) =>
        ReadZip64EndRecord(content, eocdPosition, classic) is { } zip64
            ? zip64
            : HasZip64Locator(content, eocdPosition) ? null : classic;

    /// <summary>Vérifie que l'enregistrement de fin décrit une archive que ce détecteur sait lire.</summary>
    /// <remarks>
    /// L'ordre des conditions n'est pas indifférent : la taille est bornée avant d'être additionnée à
    /// l'offset, de sorte que la dernière comparaison ne peut pas déborder.
    /// </remarks>
    private static bool IsUsable(EndRecord end) =>
        end.Disk == 0
        && end.DirectoryDisk == 0
        && end.EntriesOnDisk == end.TotalEntries
        && end.TotalEntries <= MaxZipEntries
        && end.DirectorySize <= MaxCentralDirectoryLength
        && end.DirectoryOffset <= end.End
        && end.DirectoryOffset + end.DirectorySize == end.End;

    /// <summary>Découpe le répertoire central en entrées, ou renvoie null au premier désaccord.</summary>
    /// <remarks>
    /// Des octets restants signifient que le nombre déclaré et le répertoire sont en désaccord —
    /// l'incohérence que <see cref="ZipArchive"/> ne signale qu'après avoir tout lu.
    /// </remarks>
    private static List<ZipEntryRecord>? ParseEntries(byte[] directory, int totalEntries, long directoryOffset)
    {
        var entries = new List<ZipEntryRecord>(totalEntries);
        int position = 0;

        for (int i = 0; i < totalEntries; i++)
        {
            if (ParseEntry(directory, position, directoryOffset) is not var (entry, recordLength))
            {
                return null;
            }

            entries.Add(entry);
            position += recordLength;
        }

        return position == directory.Length && (entries.Count == 0 || entries[0].LocalHeaderOffset == 0)
            ? entries
            : null;
    }

    /// <summary>Lit une entrée du répertoire central à une position donnée.</summary>
    /// <returns>L'entrée et la longueur de son enregistrement, ou null lorsqu'elle n'est pas lisible.</returns>
    private static (ZipEntryRecord Entry, int RecordLength)? ParseEntry(
        ReadOnlySpan<byte> directory, int position, long directoryOffset)
    {
        if (position + CentralHeaderLength > directory.Length)
        {
            return null;
        }

        ReadOnlySpan<byte> entry = directory[position..];

        if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != CentralHeaderSignature)
        {
            return null;
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[28..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[30..]);
        ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[32..]);
        int recordLength = CentralHeaderLength + nameLength + extraLength + commentLength;

        if (position + recordLength > directory.Length)
        {
            return null;
        }

        (long Uncompressed, long Compressed, long LocalHeaderOffset)? resolved = ResolveZip64Sizes(
            BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[42..]),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[34..]),
            entry.Slice(CentralHeaderLength + nameLength, extraLength));

        if (resolved is not { } sizes || sizes.LocalHeaderOffset >= directoryOffset)
        {
            return null;
        }

        var record = new ZipEntryRecord(
            Encoding.UTF8.GetString(entry.Slice(CentralHeaderLength, nameLength)),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[10..]),
            sizes.Compressed,
            sizes.Uncompressed,
            sizes.LocalHeaderOffset);

        return (record, recordLength);
    }

    /// <summary>Lit l'enregistrement de fin ZIP64, ou null lorsque l'archive n'en a pas ou qu'il n'est pas digne de confiance.</summary>
    /// <remarks>
    /// Chaque champ 32 bits doit soit contenir la sentinelle, soit concorder avec son homologue
    /// 64 bits — Info-ZIP écrit les deux. Deux enregistrements en désaccord décrivent deux archives,
    /// et un lecteur qui en choisit un est précisément le lecteur que vise un attaquant.
    /// </remarks>
    private static EndRecord? ReadZip64EndRecord(Stream content, long eocdPosition, EndRecord classic)
    {
        long locatorPosition = eocdPosition - Zip64LocatorLength;
        Span<byte> locator = stackalloc byte[Zip64LocatorLength];

        if (locatorPosition < 0
            || !ReadInto(content, locatorPosition, locator)
            || BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64LocatorSignature)
        {
            return null;
        }

        uint recordDisk = BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]);
        ulong recordOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        uint totalDisks = BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]);
        Span<byte> record = stackalloc byte[Zip64EndOfCentralDirectoryLength];

        if (recordDisk != 0
            || totalDisks > 1
            || recordOffset + 12 > (ulong)locatorPosition
            || !ReadInto(content, (long)recordOffset, record)
            || BinaryPrimitives.ReadUInt32LittleEndian(record) != Zip64EndOfCentralDirectorySignature)
        {
            return null;
        }

        // La taille exclut les 12 premiers octets. Elle doit se refermer exactement sur le
        // localisateur, ce qui est vérifié sans une addition qu'une taille falsifiée pourrait faire
        // déborder.
        ulong recordSize = BinaryPrimitives.ReadUInt64LittleEndian(record[4..]);

        if (recordSize < Zip64EndOfCentralDirectoryLength - 12
            || recordSize != (ulong)locatorPosition - recordOffset - 12)
        {
            return null;
        }

        var zip64 = new EndRecord(
            Disk: BinaryPrimitives.ReadUInt32LittleEndian(record[16..]),
            DirectoryDisk: BinaryPrimitives.ReadUInt32LittleEndian(record[20..]),
            EntriesOnDisk: BinaryPrimitives.ReadUInt64LittleEndian(record[24..]),
            TotalEntries: BinaryPrimitives.ReadUInt64LittleEndian(record[32..]),
            DirectorySize: BinaryPrimitives.ReadUInt64LittleEndian(record[40..]),
            DirectoryOffset: BinaryPrimitives.ReadUInt64LittleEndian(record[48..]),
            End: recordOffset);

        bool agrees = Agrees(classic.Disk, Sentinel16, zip64.Disk)
            && Agrees(classic.DirectoryDisk, Sentinel16, zip64.DirectoryDisk)
            && Agrees(classic.EntriesOnDisk, Sentinel16, zip64.EntriesOnDisk)
            && Agrees(classic.TotalEntries, Sentinel16, zip64.TotalEntries)
            && Agrees(classic.DirectorySize, Sentinel32, zip64.DirectorySize)
            && Agrees(classic.DirectoryOffset, Sentinel32, zip64.DirectoryOffset);

        return agrees ? zip64 : null;

        static bool Agrees(ulong narrow, ulong sentinel, ulong wide) => narrow == sentinel || narrow == wide;
    }

    /// <summary>Indique si un localisateur ZIP64 précède l'enregistrement de fin, qu'il soit valide ou non.</summary>
    /// <remarks>
    /// Un localisateur que <see cref="ReadZip64EndRecord"/> a refusé ne doit pas entraîner un repli
    /// sur l'enregistrement 32 bits : l'archive a déclaré des valeurs 64 bits, et lire les valeurs
    /// étroites à la place décrirait une autre archive.
    /// </remarks>
    private static bool HasZip64Locator(Stream content, long eocdPosition)
    {
        Span<byte> signature = stackalloc byte[4];

        return eocdPosition >= Zip64LocatorLength
            && ReadInto(content, eocdPosition - Zip64LocatorLength, signature)
            && BinaryPrimitives.ReadUInt32LittleEndian(signature) == Zip64LocatorSignature;
    }

    /// <summary>Remplace les tailles et l'offset sentinelles par leurs valeurs ZIP64, ou null lorsqu'elles sont absentes.</summary>
    /// <remarks>
    /// Le champ étendu ne liste que les valeurs dont l'emplacement 32 bits contient la sentinelle,
    /// toujours dans le même ordre : taille décompressée, taille compressée, offset de l'en-tête
    /// local, disque de départ. Une sentinelle sans sa valeur, ou un disque de départ différent de
    /// zéro, n'est pas une entrée que ce détecteur sait situer.
    /// </remarks>
    private static (long Uncompressed, long Compressed, long LocalHeaderOffset)? ResolveZip64Sizes(
        uint uncompressed, uint compressed, uint localHeaderOffset, ushort startDisk, ReadOnlySpan<byte> extra)
    {
        int needed = (uncompressed == Sentinel32 ? 8 : 0)
            + (compressed == Sentinel32 ? 8 : 0)
            + (localHeaderOffset == Sentinel32 ? 8 : 0)
            + (startDisk == Sentinel16 ? 4 : 0);

        if (needed == 0)
        {
            return startDisk == 0 ? (uncompressed, compressed, localHeaderOffset) : null;
        }

        ReadOnlySpan<byte> field = FindExtraField(extra, Zip64ExtraFieldId);

        if (field.Length < needed)
        {
            return null;
        }

        long resolvedUncompressed = uncompressed == Sentinel32 ? Next(ref field) : uncompressed;
        long resolvedCompressed = compressed == Sentinel32 ? Next(ref field) : compressed;
        long resolvedOffset = localHeaderOffset == Sentinel32 ? Next(ref field) : localHeaderOffset;
        uint resolvedDisk = startDisk == Sentinel16 ? BinaryPrimitives.ReadUInt32LittleEndian(field) : startDisk;

        return resolvedUncompressed < 0 || resolvedCompressed < 0 || resolvedOffset < 0 || resolvedDisk != 0
            ? null
            : (resolvedUncompressed, resolvedCompressed, resolvedOffset);

        static long Next(ref ReadOnlySpan<byte> values)
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(values);
            values = values[8..];
            return value;
        }
    }

    /// <summary>Localise un champ étendu par son identifiant, ou rend une portée vide.</summary>
    /// <remarks>
    /// Une portée vide vaut « absent » : l'appelant compare sa longueur à ce dont il a besoin, et une
    /// longueur nulle échoue à cette comparaison comme le ferait un champ tronqué.
    /// </remarks>
    private static ReadOnlySpan<byte> FindExtraField(ReadOnlySpan<byte> extra, ushort wanted)
    {
        while (extra.Length >= 4)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);

            if (4 + size > extra.Length)
            {
                return default;
            }

            if (id == wanted)
            {
                return extra.Slice(4, size);
            }

            extra = extra[(4 + size)..];
        }

        return default;
    }

    /// <summary>Lit les déclarations du manifeste — nom de partie pour les surcharges, type de contenu pour toutes.</summary>
    /// <remarks>
    /// La décompression est bornée sur la sortie, pas sur la taille déclarée : une taille déclarée
    /// est écrite par celui qui a construit le fichier. Le lecteur XML refuse les DTD et ne résout
    /// rien, si bien qu'aucune entité ne peut s'étendre et qu'aucune ressource externe ne peut être
    /// récupérée.
    /// </remarks>
    private static List<(string? PartName, string ContentType)>? ReadManifest(Stream content, ZipEntryRecord manifest)
    {
        if ((manifest.Flags & EncryptedFlag) != 0
            || manifest.Method is not (Stored or Deflated)
            || manifest.CompressedSize > MaxManifestLength
            || manifest.UncompressedSize > MaxManifestLength)
        {
            return null;
        }

        Span<byte> local = stackalloc byte[LocalHeaderLength];

        if (!ReadInto(content, manifest.LocalHeaderOffset, local)
            || BinaryPrimitives.ReadUInt32LittleEndian(local) != LocalHeaderSignature)
        {
            return null;
        }

        long dataOffset = manifest.LocalHeaderOffset + (long)LocalHeaderLength
            + BinaryPrimitives.ReadUInt16LittleEndian(local[26..])
            + BinaryPrimitives.ReadUInt16LittleEndian(local[28..]);

        if (ReadAt(content, dataOffset, (int)manifest.CompressedSize) is not { } compressed)
        {
            return null;
        }

        byte[] xml;
        try
        {
            xml = manifest.Method == Stored ? compressed : Inflate(compressed, MaxManifestLength);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return xml.Length == manifest.UncompressedSize ? ReadContentTypes(xml) : null;
    }

    /// <summary>Extrait les couples (partie, type de contenu) du manifeste déjà décompressé.</summary>
    private static List<(string? PartName, string ContentType)>? ReadContentTypes(byte[] xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxManifestLength,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        var contentTypes = new List<(string? PartName, string ContentType)>();

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml, writable: false), settings);

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.LocalName is "Default" or "Override"
                    && reader.GetAttribute("ContentType") is { } contentType)
                {
                    contentTypes.Add((reader.GetAttribute("PartName"), contentType));
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        return contentTypes;
    }

    /// <summary>Décompresse au plus <paramref name="limit"/> octets, plus un pour détecter un dépassement.</summary>
    /// <remarks>
    /// Le tampon de travail vient du pool : il fait toujours la taille de la borne, alors qu'un
    /// manifeste réel en occupe quelques pour cent. Seul le résultat, à sa taille exacte, est alloué.
    /// </remarks>
    private static byte[] Inflate(byte[] compressed, int limit)
    {
        using var inflater = new DeflateStream(new MemoryStream(compressed, writable: false), CompressionMode.Decompress);
        byte[] output = ArrayPool<byte>.Shared.Rent(limit + 1);

        try
        {
            // Un octet au-delà de la limite signifie que l'entrée se décompresse au-delà ; le contrôle
            // de taille de l'appelant échoue alors, car aucun manifeste accepté n'est aussi volumineux.
            int read = inflater.ReadAtLeast(output.AsSpan(0, limit + 1), limit + 1, throwOnEndOfStream: false);

            return output.AsSpan(0, read).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(output);
        }
    }

    /// <summary>Lit exactement <paramref name="count"/> octets à un offset donné, ou null lorsqu'ils ne sont pas présents.</summary>
    /// <remarks>
    /// Réservé aux lectures dont la taille vient du fichier. Pour une taille connue à la compilation,
    /// <see cref="ReadInto"/> évite l'allocation.
    /// </remarks>
    private static byte[]? ReadAt(Stream content, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > content.Length)
        {
            return null;
        }

        byte[] buffer = new byte[count];

        return ReadInto(content, offset, buffer) ? buffer : null;
    }

    /// <summary>Lit de quoi remplir <paramref name="buffer"/> à un offset donné.</summary>
    /// <returns>Vrai lorsque le tampon a été rempli en entier.</returns>
    private static bool ReadInto(Stream content, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > content.Length)
        {
            return false;
        }

        content.Position = offset;

        return content.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }

    /// <summary>Distingue les formats Office historiques qui partagent la signature OLE2.</summary>
    /// <remarks>
    /// <para>
    /// <c>D0 CF 11 E0</c> identifie le conteneur : .doc, .xls, .ppt et .msi sont le même format à ce
    /// niveau. La variante est donnée par le nom d'un flux situé directement sous le stockage racine.
    /// </para>
    /// <para>
    /// Seuls les enfants directs de la racine comptent. Un .xls qui incorpore un document Word
    /// contient lui aussi un flux <c>WordDocument</c>, un stockage plus bas ; un balayage d'octets ne
    /// peut pas distinguer les deux, et trouvait auparavant le nom dans le texte du document aussi
    /// facilement que dans le répertoire.
    /// </para>
    /// <para>
    /// La chaîne du répertoire est suivie à travers la table d'allocation, avec des bornes strictes
    /// sur les secteurs, les sauts DIFAT et la taille de l'arbre. Toute incohérence donne
    /// <c>ole2</c> — un conteneur reconnu qu'aucune politique n'accepte — plutôt qu'une supposition
    /// ou une exception.
    /// </para>
    /// </remarks>
    private static DetectedFormat DetectCompoundFileFlavour(Stream content, ReadOnlySpan<byte> header)
    {
        if (ReadCompoundRootStreams(content, header) is not { } streams)
        {
            return GenericCompoundFile;
        }

        DetectedFormat? match = null;
        int matches = 0;

        if (streams.Contains("WordDocument"))
        {
            match = LegacyWord;
            matches++;
        }

        if (streams.Contains("Workbook"))
        {
            match = LegacyExcel;
            matches++;
        }

        if (streams.Contains("PowerPoint Document"))
        {
            match = LegacyPowerPoint;
            matches++;
        }

        // Aucun : un .msi ou un autre conteneur. Plusieurs : un fichier qui revendique deux formats à la fois.
        return matches == 1 ? match! : GenericCompoundFile;
    }

    /// <summary>Lit les noms des flux situés directement sous le stockage racine, ou null lorsqu'ils ne sont pas dignes de confiance.</summary>
    /// <remarks>
    /// Disposition de l'en-tête selon [MS-CFB] 2.2 : la version 3 utilise des secteurs de 512 octets,
    /// la version 4 des secteurs de 4096 octets, et les deux doivent concorder. Le secteur <c>n</c>
    /// commence à <c>(n + 1) × taille de secteur</c>.
    /// </remarks>
    private static HashSet<string>? ReadCompoundRootStreams(Stream content, ReadOnlySpan<byte> header)
    {
        if (header.Length < 512)
        {
            return null;
        }

        ushort majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1A..]);
        ushort byteOrder = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1C..]);
        ushort sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1E..]);

        if (byteOrder != 0xFFFE || !((majorVersion == 3 && sectorShift == 9) || (majorVersion == 4 && sectorShift == 12)))
        {
            return null;
        }

        if (ReadDirectory(content, header, sectorShift) is not { } entries)
        {
            return null;
        }

        // L'entrée 0 est le stockage racine (type 5) ; son enfant est le sommet d'un arbre de ses
        // enfants, reliés par les frères gauche et droit.
        return entries.Length >= DirectoryEntryLength && entries[0x42] == 5
            ? CollectRootStreams(entries)
            : null;
    }

    /// <summary>Lit les secteurs du répertoire, dans l'ordre de la chaîne, ou null au premier doute.</summary>
    /// <remarks>
    /// La chaîne est parcourue avant d'être lue : le nombre de secteurs est alors connu, et le
    /// répertoire tient dans une seule allocation à sa taille exacte plutôt que dans une liste qui
    /// double et se recopie. Un secteur hors bornes échoue à l'une ou l'autre des deux passes, et les
    /// deux répondent null.
    /// </remarks>
    private static byte[]? ReadDirectory(Stream content, ReadOnlySpan<byte> header, int sectorShift)
    {
        Span<uint> chain = stackalloc uint[MaxCompoundDirectorySectors];
        int count = 0;
        uint sector = BinaryPrimitives.ReadUInt32LittleEndian(header[0x30..]);

        while (sector != EndOfChain)
        {
            if (sector > MaxRegularSector || count == MaxCompoundDirectorySectors || Contains(chain[..count], sector))
            {
                return null;
            }

            chain[count++] = sector;

            if (NextSector(content, header, sectorShift, sector) is not { } next)
            {
                return null;
            }

            sector = next;
        }

        int sectorSize = 1 << sectorShift;
        byte[] directory = new byte[count * sectorSize];

        for (int i = 0; i < count; i++)
        {
            if (!ReadInto(content, ((long)chain[i] + 1) << sectorShift, directory.AsSpan(i * sectorSize, sectorSize)))
            {
                return null;
            }
        }

        return directory;

        static bool Contains(ReadOnlySpan<uint> seen, uint sector) => seen.IndexOf(sector) >= 0;
    }

    /// <summary>Parcourt l'arbre des enfants de la racine et rend les noms de ceux qui sont des flux.</summary>
    /// <remarks>
    /// Seuls les frères gauche et droit sont suivis : descendre dans l'enfant d'une entrée mènerait
    /// aux flux d'un stockage imbriqué, que la racine ne possède pas directement.
    /// </remarks>
    private static HashSet<string>? CollectRootStreams(byte[] entries)
    {
        int entryCount = entries.Length / DirectoryEntryLength;
        var streams = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<uint>();
        var pending = new Stack<uint>();
        pending.Push(BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(0x4C)));

        while (pending.Count > 0)
        {
            uint id = pending.Pop();

            if (id == NoStream)
            {
                continue;
            }

            if (id == 0 || id >= entryCount || !seen.Add(id))
            {
                return null;
            }

            ReadOnlySpan<byte> entry = entries.AsSpan((int)id * DirectoryEntryLength, DirectoryEntryLength);
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[0x40..]);

            if (nameLength is < 2 or > 64 || nameLength % 2 != 0)
            {
                return null;
            }

            if (entry[0x42] == 2)
            {
                streams.Add(Encoding.Unicode.GetString(entry[..(nameLength - 2)]));
            }

            pending.Push(BinaryPrimitives.ReadUInt32LittleEndian(entry[0x44..]));
            pending.Push(BinaryPrimitives.ReadUInt32LittleEndian(entry[0x48..]));
        }

        return streams;
    }

    /// <summary>L'entrée de la table d'allocation pour un secteur, ou null lorsqu'elle est hors bornes.</summary>
    private static uint? NextSector(Stream content, ReadOnlySpan<byte> header, int sectorShift, uint current)
    {
        uint entriesPerSector = (uint)(1 << sectorShift) / 4;

        if (FatSectorFor(content, header, sectorShift, current / entriesPerSector) is not { } fatSector
            || fatSector > MaxRegularSector)
        {
            return null;
        }

        Span<byte> value = stackalloc byte[4];
        long offset = (((long)fatSector + 1) << sectorShift) + (4 * (current % entriesPerSector));

        return ReadInto(content, offset, value) ? BinaryPrimitives.ReadUInt32LittleEndian(value) : null;
    }

    /// <summary>Localise le secteur de table d'allocation d'un index, via la DIFAT de l'en-tête puis sa chaîne.</summary>
    /// <remarks>
    /// Les 109 premiers index sont dans l'en-tête déjà lu. Au-delà, chaque secteur DIFAT contient
    /// <c>entriesPerSector - 1</c> index, son dernier emplacement pointant vers le suivant.
    /// </remarks>
    private static uint? FatSectorFor(Stream content, ReadOnlySpan<byte> header, int sectorShift, uint fatIndex)
    {
        if (fatIndex < HeaderDifatSlots)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(header[(HeaderDifatOffset + (4 * (int)fatIndex))..]);
        }

        uint entriesPerSector = (uint)(1 << sectorShift) / 4;
        uint remaining = fatIndex - HeaderDifatSlots;
        uint difatSector = BinaryPrimitives.ReadUInt32LittleEndian(header[0x44..]);
        Span<byte> slot = stackalloc byte[4];
        int hops = 0;

        while (remaining >= entriesPerSector - 1)
        {
            long link = (((long)difatSector + 1) << sectorShift) + (1 << sectorShift) - 4;

            if (++hops > MaxCompoundDifatHops || difatSector > MaxRegularSector || !ReadInto(content, link, slot))
            {
                return null;
            }

            difatSector = BinaryPrimitives.ReadUInt32LittleEndian(slot);
            remaining -= entriesPerSector - 1;
        }

        long offset = (((long)difatSector + 1) << sectorShift) + (4 * remaining);

        return difatSector <= MaxRegularSector && ReadInto(content, offset, slot)
            ? BinaryPrimitives.ReadUInt32LittleEndian(slot)
            : null;
    }

    /// <summary>Ce que le répertoire central indique sur une entrée — les seuls faits dont l'identification a besoin.</summary>
    private sealed record ZipEntryRecord(
        string Name,
        ushort Flags,
        ushort Method,
        long CompressedSize,
        long UncompressedSize,
        long LocalHeaderOffset);

    /// <summary>Les faits de l'enregistrement de fin, élargis pour que les formes 32 bits et ZIP64 partagent un même contrôle.</summary>
    /// <param name="Disk">Le numéro de ce disque.</param>
    /// <param name="DirectoryDisk">Le disque où commence le répertoire central.</param>
    /// <param name="EntriesOnDisk">Les entrées présentes sur ce disque.</param>
    /// <param name="TotalEntries">Les entrées de l'archive entière.</param>
    /// <param name="DirectorySize">La longueur du répertoire central, en octets.</param>
    /// <param name="DirectoryOffset">L'endroit où commence le répertoire central.</param>
    /// <param name="End">L'endroit où commence l'enregistrement qui suit le répertoire central.</param>
    private sealed record EndRecord(
        ulong Disk,
        ulong DirectoryDisk,
        ulong EntriesOnDisk,
        ulong TotalEntries,
        ulong DirectorySize,
        ulong DirectoryOffset,
        ulong End);
}
