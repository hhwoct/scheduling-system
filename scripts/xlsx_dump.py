import zipfile, re, sys, json
from xml.etree import ElementTree as ET
NS='{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
def col_to_idx(ref):
    m=re.match(r'([A-Z]+)(\d+)',ref)
    c,r=m.group(1),int(m.group(2))
    n=0
    for ch in c: n=n*26+(ord(ch)-64)
    return n-1, r-1
def read(path):
    z=zipfile.ZipFile(path)
    shared=[]
    if 'xl/sharedStrings.xml' in z.namelist():
        root=ET.fromstring(z.read('xl/sharedStrings.xml'))
        for si in root.findall(NS+'si'):
            txt=''.join(t.text or '' for t in si.iter(NS+'t'))
            shared.append(txt)
    wb=ET.fromstring(z.read('xl/workbook.xml'))
    rels=ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))
    rmap={r.get('Id'):r.get('Target') for r in rels}
    sheets=[]
    for sh in wb.find(NS+'sheets'):
        rid=sh.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id')
        tgt=rmap[rid]
        if not tgt.startswith('xl/'): tgt='xl/'+tgt.lstrip('/')
        sheets.append((sh.get('name'), tgt))
    out={}
    for name,tgt in sheets:
        root=ET.fromstring(z.read(tgt))
        grid={}
        maxr=maxc=0
        for c in root.iter(NS+'c'):
            ref=c.get('r');
            if not ref: continue
            ci,ri=col_to_idx(ref)
            t=c.get('t')
            v=c.find(NS+'v'); isel=c.find(NS+'is')
            val=''
            if t=='s' and v is not None: val=shared[int(v.text)]
            elif t=='inlineStr' and isel is not None: val=''.join(x.text or '' for x in isel.iter(NS+'t'))
            elif v is not None: val=v.text
            if val is None: val=''
            val=str(val).strip()
            if val!='':
                grid[(ri,ci)]=val
                maxr=max(maxr,ri); maxc=max(maxc,ci)
        merges=[]
        mc=root.find(NS+'mergeCells')
        if mc is not None:
            merges=[m.get('ref') for m in mc]
        rows=[[grid.get((r,c),'') for c in range(maxc+1)] for r in range(maxr+1)]
        out[name]={'rows':rows,'merges':merges}
    return out
if __name__=='__main__':
    data=read(sys.argv[1])
    print(json.dumps({k:{'rows':v['rows'],'merges':v['merges'][:40],'nmerge':len(v['merges'])} for k,v in data.items()}, ensure_ascii=False))
