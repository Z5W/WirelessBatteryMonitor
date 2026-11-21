"""Build app-private mixed-script fonts from official OFL sources (fontTools required).
Chinese and other non-Latin glyphs: Source Han Sans SC; Latin glyphs: Noto Sans CJK SC.
Run: python tools/build-ui-fonts.py
Sources are cached under artifacts/temp/font-sources; derived fonts are embedded in the EXE.
"""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
import hashlib, json
root=Path(__file__).resolve().parents[1]
sources=root/'artifacts/temp/font-sources'
out=root/'src/WirelessBatteryMoniter/Assets/Fonts'
latin=set(range(0x250)) | set(range(0x1e00,0x1f00))
report={'sources':{},'faces':{}}
for name in ['SourceHanSansSC-VF.ttf','NotoSansSC-VF.ttf']:
    report['sources'][name]=hashlib.sha256((sources/name).read_bytes()).hexdigest()
for weight,style in [(400,'Regular'),(700,'Bold')]:
    base=instantiateVariableFont(TTFont(sources/'SourceHanSansSC-VF.ttf'),{'wght':weight},inplace=True)
    donor=instantiateVariableFont(TTFont(sources/'NotoSansSC-VF.ttf'),{'wght':weight},inplace=True)
    assert base['head'].unitsPerEm==donor['head'].unitsPerEm
    mapping,other=base.getBestCmap(),donor.getBestCmap()
    glyphs=donor.getGlyphSet();replaced=0
    for cp in sorted(latin & mapping.keys() & other.keys()):
        recording=DecomposingRecordingPen(glyphs)
        glyphs[other[cp]].draw(recording)
        pen=TTGlyphPen(None);recording.replay(pen)
        base['glyf'][mapping[cp]]=pen.glyph()
        base['hmtx'][mapping[cp]]=donor['hmtx'][other[cp]]
        replaced+=1
    names=base['name']
    # Rename the modified font family, preserving original copyright and license records.
    replacements={1:'WBM Mixed Sans',2:style,3:'WBM Mixed Sans '+style+' 1.0',4:'WBM Mixed Sans '+style,6:'WBMMixedSans-'+style,16:'WBM Mixed Sans',17:style}
    for record in list(names.names):
        if record.nameID in replacements:
            names.setName(replacements[record.nameID],record.nameID,record.platformID,record.platEncID,record.langID)
    for nid,value in replacements.items(): names.setName(value,nid,3,1,0x409)
    base['head'].macStyle=1 if weight==700 else 0
    base['OS/2'].usWeightClass=weight
    base['OS/2'].fsSelection=(base['OS/2'].fsSelection & ~0x61) | (0x20 if weight==700 else 0x40)
    if 'DSIG' in base: del base['DSIG']
    path=out/('WBMMixedSans-'+style+'.ttf');base.save(path)
    report['faces'][style]={'latin_glyphs_from_noto':replaced,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
    print(style,replaced,path.stat().st_size,flush=True)
(out/'font-build.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
