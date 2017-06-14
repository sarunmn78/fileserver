from os import listdir
from os.path import isfile, join
import uuid
import json
import os

file_list = []

#mypath = "data/"
#onlyfiles = [f for f in listdir(mypath) if isfile(join(mypath, f))]
#for fname in onlyfiles:
#    file_list.append({"file_id" : str(uuid.uuid4()), "file_name" : fname})

#with open('data/filelist.json', 'w') as outfile:
#    json.dump(file_list, outfile)

def get_transferlist():
    transfer_list = []
    transfer_filename = 'tempfiles/transferlist.json'
    if os.path.exists(transfer_filename):
        with open(transfer_filename) as data_file:    
            transfer_list = json.load(data_file)
    return transfer_list        

def init_upload(fname):
    transfer_list = get_transferlist()
    file_id = str(uuid.uuid4())
    transfer_list.append({"file_id" : file_id, "file_name" : fname})

    with open(transfer_filename, 'w') as outfile:
        json.dump(transfer_list, outfile)

    return file_id

def append_to_file(file_id, data):
    fname = "";
    transfer_list = get_transferlist()
    fnames = [f["file_name"] for f in transfer_list if f["file_id"] == file_id]
    if len(fnames) > 0:
        fname = fnames[0]
    else:
        return {"error" : "file id is invalid"}
    
    
    with open("tempfiles/" + fname, 'ab') as outfile:
        outfile.write(data)
        outfile.close()
        return {"error" : "success"}

    return {"error" : "permission error"}
    

#file_id = init_upload()
#print file_id   

f = open("sample.jpg", 'rb')
while True:
    piece = f.read(1024)  
    if not piece:
        break
    print "append_to_file: " + str(len(piece))
    append_to_file("45807b63-bbf1-4c8b-ba8f-c1dbaf90ab27", piece)
f.close()
