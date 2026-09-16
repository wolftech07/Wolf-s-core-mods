import marshal, dis, sys
c = marshal.loads(open(sys.argv[1], 'rb').read())
with open(sys.argv[2], 'w', encoding='utf-8') as f:
    dis.dis(c, file=f)
